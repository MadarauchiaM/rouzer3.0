using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Modelos
{
    public class CrearHiloViewModel : CrearViewModel
    {
        [MinLength(1)]
        [Required(ErrorMessage = "Tienes que especificar un titulo padre")]
        [MaxLength(100, ErrorMessage = "El titulo es muy largo padre, 100 caracteres maximo")]
        public string Titulo { get; set; }
        [Required(ErrorMessage = "Tienes que escribir un redactazo, o por lo menos un caracter")]
        [MaxLength(10000, ErrorMessage = "El redactazo es muy largo padre, 10000 caracteres maximo")]
        public new string Contenido { get; set; }
        [Required(ErrorMessage = "Debes elegir una categoria para su hilo anon")]
        public int CategoriaId { get; set; }
        public string Encuesta { get; set; }
        public string Captcha { get; set; }
    }
}