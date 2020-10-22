using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore;
using Microsoft.Extensions.Logging;
using Servicios;
using System.Collections.Generic;
using Modelos;
using System.Threading.Tasks;
using System.Net;
using System;
using WebApp;
using Microsoft.AspNetCore.Authorization;
using Data;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using System.IO;

namespace WebApp.Controllers
{
    [Authorize("esAdmin")]
    [ApiController, Route("api/Administracion/{action}/{id?}")]
    public class Administracion : Controller
    {
        private readonly IHiloService hiloService;
        private readonly IMediaService mediaService;
        private readonly HashService hashService;
        private readonly IHubContext<RChanHub> rchanHub;
        private readonly RChanContext context;
        private readonly UserManager<UsuarioModel> userManager;
        private readonly SignInManager<UsuarioModel> signInManager;

        public Administracion(
            IHiloService hiloService,
            IMediaService mediaService,
            HashService hashService,
            IHubContext<RChanHub> rchanHub,
            RChanContext context,
            UserManager<UsuarioModel> userManager,
            SignInManager<UsuarioModel> signInManager
        )
        {
            this.hiloService = hiloService;
            this.mediaService = mediaService;
            this.hashService = hashService;
            this.rchanHub = rchanHub;
            this.context = context;
            this.userManager = userManager;
            this.signInManager = signInManager;
        }
        [Route("/Administracion")]
        public async Task<ActionResult> Index()
        {
            var admins = await userManager.GetUsersForClaimAsync(new Claim("Role", "admin"));
            var mods = await userManager.GetUsersForClaimAsync(new Claim("Role", "mod"));
            var vm = new AdministracionVM
            {
                Admins = admins.Select(u => new UsuarioVM { Id = u.Id, UserName = u.UserName }).ToArray(),
                Mods = mods.Select(u => new UsuarioVM { Id = u.Id, UserName = u.UserName }).ToArray()
            };
            return View(vm);
        }

        public class RolUserVM
        {
            public string Username { get; set; }
            public string Role { get; set; }
        }
        [HttpPost]
        public async Task<ActionResult> AñadirRol(RolUserVM model)
        {
            var role = model.Role;
            var username = model.Username;

            if (!new[] { "admin", "mod", "ayudante" }.Contains(role))
                ModelState.AddModelError("Rol", "Rol invalido");


            var user = await userManager.Users.FirstOrDefaultAsync(u => u.UserName == username || u.Id == username);
            if (user is null)
                ModelState.AddModelError("userName", "No se encontre al usuario");
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            bool yaTieneElRol = (await userManager.GetClaimsAsync(user)).FirstOrDefault(c => c.Type == "Role") != null;

            #region debg
            var claims = await userManager.GetClaimsAsync(user);
            System.Console.WriteLine(claims);
            #endregion

            if (yaTieneElRol)
            {
                ModelState.AddModelError("", "El usuario ya tiene ese rol");
                return BadRequest(ModelState);
            }

            var result = await userManager.AddClaimAsync(user, new Claim("Role", role));
            if (result.Succeeded)
            {
                await signInManager.RefreshSignInAsync(user);
                return Json(new ApiResponse($"{user.UserName} ahora es {role}"));
            }
            else
                return BadRequest(result.Errors);
        }
        public async Task<ActionResult> RemoverRol(RolUserVM model)
        {
            var role = model.Role;
            var username = model.Username;

            if (!new[] { "admin", "mod", "ayudante" }.Contains(role))
                ModelState.AddModelError("Rol", "Rol invalido");


            var user = await userManager.Users.FirstOrDefaultAsync(u => u.UserName == username || u.Id == username);
            if (user is null)
                ModelState.AddModelError("userName", "No se encontre al usuario");
            if (!ModelState.IsValid)
                return BadRequest(ModelState);


            var result = await userManager.RemoveClaimAsync(user, new Claim("Role", role));
            if (result.Succeeded)
            {
                await signInManager.RefreshSignInAsync(user);
                return Json(new ApiResponse($"{user.UserName} ya no es {role}"));
            }
            else
                return BadRequest(result.Errors);
        }

        [HttpPost]
        public async Task<ActionResult> AñadirSticky(Sticky sticky)
        {
            var hilo = await context.Hilos.FirstOrDefaultAsync(h => h.Id == sticky.HiloId);
            if (hilo is null) ModelState.AddModelError("Hilo", "El hilo no existe");
            if (!ModelState.IsValid) return BadRequest(ModelState);

            context.Stickies.RemoveRange(context.Stickies.Where(s => s.HiloId == sticky.HiloId));
            await context.SaveChangesAsync();

            if (sticky.Importancia == 0) return Json(new ApiResponse("Sticky removido"));

            context.Add(sticky);
            await context.SaveChangesAsync();
            return Json(new ApiResponse("Hilo stickeado"));
        }

        public class BorrarHiloVm
        {
            public string HiloId { get; set; }
        }
        public async Task<ActionResult> BorrarHilo(BorrarHiloVm vm)
        {
            var hilo = await context.Hilos.FirstOrDefaultAsync(h => h.Id == vm.HiloId);
            if (hilo is null) return NotFound();

            hilo.Estado = HiloEstado.Eliminado;

            await context.SaveChangesAsync();
            return Json(new ApiResponse("Hilo borrado"));
        }

        public class CambiarCategoriaVm
        {
            public string HiloId { get; set; }
            public int CategoriaId { get; set; }
        }
        public async Task<ActionResult> CambiarCategoria(CambiarCategoriaVm vm)
        {
            var hilo = await context.Hilos.FirstOrDefaultAsync(h => h.Id == vm.HiloId);
            if (hilo is null) return NotFound();

            if(!Constantes.Categorias.Any(c => c.Id == vm.CategoriaId))
            {
                ModelState.AddModelError("Categoria", "La categoria es invalida");
                return Json(ModelState);
            }

            hilo.CategoriaId = vm.CategoriaId;

            await context.SaveChangesAsync();
            return Json(new ApiResponse("Categoria cambiada!"));
        }
    }
}

public class AdministracionVM
{
    public UsuarioVM[] Admins { get; set; }
    public UsuarioVM[] Mods { get; set; }
}

public class UsuarioVM
{
    public string Id { get; set; }
    public string UserName { get; set; }
}

