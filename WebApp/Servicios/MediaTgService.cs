using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Modelos;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.InputFiles;
using File = Telegram.Bot.Types.File;

namespace Servicios
{
    public class MediaTgService : MediaService
    {
        private TelegramBotClient bot;
        private static ChatId chat;

        public MediaTgService(
            string carpetaDeAlmacenamiento,
            RChanContext context,
            IWebHostEnvironment env,
            ILogger<MediaService> logger,
            IConfiguration conf) : base(carpetaDeAlmacenamiento, context, env, logger)
        {
            bot = new TelegramBotClient(conf.GetValue<string>("Telegram:BotId"));
            chat = new ChatId(conf.GetValue<long>("Telegram:ChatId"));
        }

        public override async Task<MediaModel> GenerarMediaDesdeArchivo(IFormFile archivo, bool esAdmin)
        {
            bool esVideo = archivo.ContentType.Contains("video");

            using var archivoStream = archivo.OpenReadStream();

            Stream imagenStream;

            if (!esVideo && !archivo.FileName.Contains(".gif"))
                imagenStream = archivoStream;
            else
                imagenStream = await GenerarImagenDesdeVideo(archivoStream, archivo.FileName);

            // Genero un hash md5 del archivo
            archivoStream.Seek(0, SeekOrigin.Begin);
            var hash = await archivoStream.GenerarHashAsync();
            // Me fijo si el hash existe en la db
            var mediaAntiguo = await context.Medias.FirstOrDefaultAsync(e => e.Id == hash);

            if (mediaAntiguo != null) return mediaAntiguo;

            // Si no se resetea el stream imageSharp deja de funcionar -_o_-
            imagenStream.Seek(0, SeekOrigin.Begin);
            archivoStream.Seek(0, SeekOrigin.Begin);

            using var original = await Image.LoadAsync(imagenStream);
            using var thumbnail = original.Clone(e => e.Resize(300, 0));
            using var cuadradito = thumbnail.Clone(e => e.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Crop,
                Position = AnchorPositionMode.Center,
                Size = new Size(300)
            }));

            var media = new MediaModel
            {
                Id = hash,
                Hash = hash,
                Tipo = esVideo ? MediaType.Video : MediaType.Imagen,
                Url = $"{hash}{Path.GetExtension(archivo.FileName)}",
            };

            // Si no se resetea el stream guarda correctamente -_o_-
            imagenStream.Seek(0, SeekOrigin.Begin);
            archivoStream.Seek(0, SeekOrigin.Begin);

            // Guardo las imagenes (original y miniatura) en el sistema de archivos
            // using var salida = File.Create($"{CarpetaDeAlmacenamiento}/{media.Url}");
            // await archivoStream.CopyToAsync(salida);
            // await thumbnail.SaveAsync($"{CarpetaDeAlmacenamiento}/{media.VistaPreviaLocal}");
            // await cuadradito.SaveAsync($"{CarpetaDeAlmacenamiento}/{media.VistaPreviaCuadradoLocal}");

            // Guardo las imagenes en tg
            var tgMedia = new TgMedia();

            var msg = await bot.SendDocumentAsync(chat, new InputOnlineFile(archivoStream, "img"));
            tgMedia.UrlTgId = msg.Document.FileId;
            media.Url += $"?t={msg.Document.FileId}";

            tgMedia.VistaPreviaCuadradoTgId = await SubirImagenATg(cuadradito);
            tgMedia.VistaPreviaTgId = await SubirImagenATg(thumbnail);

            media.TgMedia = tgMedia;

            await imagenStream.DisposeAsync();
            await archivoStream.DisposeAsync();

            await context.Medias.AddAsync(media);

            return media;
        }

        public override async Task<bool> Eliminar(string id)
        {
            var media = await context.Medias.FirstOrDefaultAsync(m => m.Id == id);
            if (media.TgMedia != null)
            {
                media.Tipo = MediaType.Eliminado;
                await context.SaveChangesAsync();
                return true;
            };

            return await base.Eliminar(id);
        }

        override public async Task<MediaModel> GenerarMediaDesdeLink(string url, bool esAdmin)
        {
            if(Regex.IsMatch(url, @"(?:youtube\.com\/\S*(?:(?:\/e(?:mbed))?\/|watch\?(?:\S*?&?v\=))|youtu\.be\/)([a-zA-Z0-9_-]{6,11})"))
            {
                return await base.GenerarMediaDesdeYouTube(url, esAdmin);
            }
            
            var res = await new HttpClient().GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            var header = res.Content.Headers;

            if(header.ContentLength > 12 * 1024 * 1024 || !EsFormatoValido(header.ContentType.MediaType))
            {
                throw new Exception("El archivo es invalido o muy pesado");
            }

            res = await new HttpClient().GetAsync(url);
            bool esVideo = header.ContentType.MediaType.Contains("video");

            using var archivoStream = await res.Content.ReadAsStreamAsync();

            Stream imagenStream;

            if (!esVideo && !header.ContentType.MediaType.Contains(".gif"))
                imagenStream = archivoStream;
            else
                imagenStream = await GenerarImagenDesdeVideo(archivoStream, new Guid().ToString());

            // Genero un hash md5 del archivo
            archivoStream.Seek(0, SeekOrigin.Begin);
            var hash = await archivoStream.GenerarHashAsync();
            // Me fijo si el hash existe en la db
            var mediaAntiguo = await context.Medias.FirstOrDefaultAsync(e => e.Id == hash);

            if (mediaAntiguo != null) return mediaAntiguo;

            // Si no se resetea el stream imageSharp deja de funcionar -_o_-
            imagenStream.Seek(0, SeekOrigin.Begin);
            archivoStream.Seek(0, SeekOrigin.Begin);

            using var original = await Image.LoadAsync(imagenStream);
            using var thumbnail = original.Clone(e => e.Resize(300, 0));
            using var cuadradito = thumbnail.Clone(e => e.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Crop,
                Position = AnchorPositionMode.Center,
                Size = new Size(300)
            }));

            var media = new MediaModel
            {
                Id = hash,
                Hash = hash,
                Tipo = esVideo ? MediaType.Video : MediaType.Imagen,
                Url = $"{hash}.{header.ContentType.MediaType.Split("/").Last()}",
            };

            // Si no se resetea el stream guarda correctamente -_o_-
            imagenStream.Seek(0, SeekOrigin.Begin);
            archivoStream.Seek(0, SeekOrigin.Begin);

            // Guardo las imagenes (original y miniatura) en el sistema de archivos
            // using var salida = File.Create($"{CarpetaDeAlmacenamiento}/{media.Url}");
            // await archivoStream.CopyToAsync(salida);
            // await thumbnail.SaveAsync($"{CarpetaDeAlmacenamiento}/{media.VistaPreviaLocal}");
            // await cuadradito.SaveAsync($"{CarpetaDeAlmacenamiento}/{media.VistaPreviaCuadradoLocal}");

            // Guardo las imagenes en tg
            var tgMedia = new TgMedia();

            var msg = await bot.SendDocumentAsync(chat, new InputOnlineFile(archivoStream, "img"));
            tgMedia.UrlTgId = msg.Document.FileId;
            media.Url += $"?t={msg.Document.FileId}";

            tgMedia.VistaPreviaCuadradoTgId = await SubirImagenATg(cuadradito);
            tgMedia.VistaPreviaTgId = await SubirImagenATg(thumbnail);

            media.TgMedia = tgMedia;

            await imagenStream.DisposeAsync();
            await archivoStream.DisposeAsync();

            await context.Medias.AddAsync(media);

            return media;

            // using var thumbnail = await vistaPrevia.Content.ReadAsStreamAsync();

            // using var cuadradito = (await Image.LoadAsync(thumbnail)).Clone(e => e.Resize(new ResizeOptions
            // {
            //     Mode = ResizeMode.Crop,
            //     Position = AnchorPositionMode.Center,
            //     Size = new Size(300)
            // }));

            // using var vistaPreviaSalida = File.Create($"{CarpetaDeAlmacenamiento}/{media.VistaPreviaLocal}");
            // thumbnail.Seek(0, SeekOrigin.Begin);

            // await thumbnail.CopyToAsync(vistaPreviaSalida);
            // await cuadradito.SaveAsync($"{CarpetaDeAlmacenamiento}/{media.VistaPreviaCuadradoLocal}");

            // return media;
        }

        private async Task<string> SubirImagenATg(Image imagen)
        {
            using var stream = new MemoryStream();
            imagen.SaveAsJpeg(stream);
            stream.Seek(0, SeekOrigin.Begin);
            var msg = await bot.SendDocumentAsync(chat, new InputOnlineFile(stream, "img.jpg"));
            return msg.Document.FileId;
        }
        public async Task<File> DescargarArchivoTg(string id, Stream stream)
        {
            var f = await bot.GetFileAsync(id);
            return await bot.GetInfoAndDownloadFileAsync(id, stream);
        }
        public async Task<File> GetInfoArchivoTg(string id)
        {
            return await bot.GetFileAsync(id);
        }
    }
}
