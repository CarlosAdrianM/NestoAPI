using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Reflection;
using System.Web.Http;
using System.Web.Http.Controllers;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Sugerencia 779 (26/10/26): Web API 2 no admite un parámetro [FromBody] con valor por defecto. Con
    /// <c>[FromBody] TerminarReposicionDTO peticion = null</c> (sugerencia 753) TODAS las llamadas a
    /// POST api/Reposiciones/EnPreparacion/Terminar fallaban con «El parámetro opcional 'peticion' no es compatible
    /// con 'FormatterParameterBinding'», y ninguna tienda podía terminar una reposición. Sin cuerpo, un parámetro
    /// [FromBody] ya llega null: no hace falta el «= null».
    /// </summary>
    [TestClass]
    public class ParametrosFromBodyOpcionalesTests
    {
        [TestMethod]
        public void Controladores_NingunParametroFromBody_TieneValorPorDefecto()
        {
            var opcionales = typeof(WebApiConfig).Assembly.GetTypes()
                .Where(t => typeof(IHttpController).IsAssignableFrom(t) && !t.IsAbstract)
                .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                .SelectMany(m => m.GetParameters()
                    .Where(p => p.IsOptional && p.GetCustomAttribute<FromBodyAttribute>() != null)
                    .Select(p => $"{m.DeclaringType.Name}.{m.Name}({p.Name})"))
                .ToList();

            Assert.AreEqual(0, opcionales.Count, "Parámetros [FromBody] opcionales (Web API los rechaza en cada llamada): " +
                string.Join(", ", opcionales));
        }
    }
}
