using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using Data;
using Modelos;
using SqlKata.Execution;
using SqlKata.Compilers;
using SqlKata;
using Dapper;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using WebApp;

namespace Servicios
{
    public interface IComentarioService
    {
        Task<string> Guardar(ComentarioModel comentario, bool bumpearHilo = true);
        Task<List<ComentarioViewModel>> DeHilo(string hiloId, string creadorId);
        Task Eliminar(params string[] ids) => Eliminar(ids, false, false);
        Task Eliminar(string[] ids, bool borrarMedias, bool borrarAudios);
        string[] GetIdsTageadas(string contenido);
    }

    public class ComentarioService : ContextService, IComentarioService
    {
        private readonly FormateadorService formateador;
        private readonly IHubContext<RChanHub> rchanHub;
        private readonly IMediaService mediaService;
        private readonly AccionesDeModeracionService historial;
        private readonly IAudioService audioService;

        public ComentarioService(RChanContext context,
            FormateadorService formateador,
            IHubContext<RChanHub> rchanHub,
            IMediaService mediaService,
            HashService hashService,
            AccionesDeModeracionService historial,
            IAudioService audioService
            )

            : base(context, hashService)
        {
            this.rchanHub = rchanHub;
            this.mediaService = mediaService;
            this.historial = historial;
            this.formateador = formateador;
            this.audioService = audioService;
        }

        public async Task<List<ComentarioViewModel>> DeHilo(string hiloId, string creadorId)
        {
            return await _context.Comentarios
                .Where(c => c.HiloId == hiloId)
                .Where(c => c.Estado == ComentarioEstado.Normal)
                .OrderByDescending(c => c.Creacion)
                .Include(c => c.Media)
                .Select(c => new ComentarioViewModel(c))
                .ToListAsync();
        }

        public async Task<string> Guardar(ComentarioModel comentario, bool bumpearHilo = true)
        {
            comentario.Contenido = formateador.Parsear(comentario.Contenido);
            comentario.Id = hashService.Random(8).ToUpper();
            _context.Comentarios.Add(comentario);
            await _context.SaveChangesAsync();

            if (!comentario.Contenido.ToLower().Contains("gt;hide"))
            {
                await db.Query("Hilos")
                    .Where("Id", comentario.HiloId)
                    .UpdateAsync(new { Bump = DateTimeOffset.Now });
            }
            return comentario.Id;
        }

        public Task Eliminar(params string[] ids) => Eliminar(ids, false);
        public async Task Eliminar(string[] ids, bool borrarMedias = false, bool borrarAudios = false)
        {
            var comentarios = await _context.Comentarios
                .Where(c => ids.Contains(c.Id))
                .Where(c => c.Estado != ComentarioEstado.Eliminado)
                .ToListAsync();

            comentarios.ForEach(c => c.Estado = ComentarioEstado.Eliminado);

            var denuncias = await _context.Denuncias
                .Where(d => ids.Contains(d.ComentarioId))
                .ToListAsync();
            denuncias.ForEach(d => d.Estado = EstadoDenuncia.Aceptada);

            if (borrarMedias)
            {
                var mediaIds = comentarios.Select(h => h.MediaId).ToArray();
                foreach (var m in mediaIds)
                {
                    await mediaService.Eliminar(m);
                }
            }

            if (borrarAudios)
            {
                var audiosIds = comentarios.Select(h => h.AudioId).ToArray();
                foreach (var a in audiosIds)
                {
                    await audioService.Eliminar(a);
                }
            }

            await rchanHub.Clients.All.SendAsync("ComentariosEliminados", ids);
            await rchanHub.Clients.Group("moderacion").SendAsync("denunciasAceptadas", denuncias.Select(d => d.Id).ToArray());
            int eliminados = await _context.SaveChangesAsync();
        }
        public string[] GetIdsTageadas(string contenido) => formateador.GetIdsTageadas(contenido);
    }

}
