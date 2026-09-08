using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Videos;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NestoAPI.Tests.Infraestructure.Videos
{
    /// <summary>
    /// EL LISTADO POR DEFECTO NO PUEDE DEVOLVER LOS VÍDEOS DE BAJA.
    ///
    /// <para>No es una preferencia de diseño, es un contrato con la tienda online. La versión del
    /// módulo que hay hoy en producción (1.6.6) pide el listado sin <c>incluirBajas</c>, no mira
    /// <c>FechaBaja</c>, y REACTIVA cualquier ficha que reaparezca en el listado. Si el listado por
    /// defecto empezara a incluir las bajas, esa versión resucitaría de un golpe las <b>84</b>
    /// fichas que ya están de baja —los 77 vídeos retirados y las 7 gemelas duplicadas— y otras
    /// tantas URL muertas volverían a responder 200 (equipo de SEO, 08/09/26).</para>
    ///
    /// <para>El equipo de SEO nos pidió confirmarlo por escrito antes de publicar. Esto es mejor que
    /// confirmarlo por escrito: si alguien lo cambia, la suite se pone roja.</para>
    /// </summary>
    [TestClass]
    public class ListadoVideosBajasTests
    {
        private static IQueryable<Video> Catalogo()
        {
            return new List<Video>
            {
                new Video { Id = 1, Titulo = "Vivo",           FechaBaja = null },
                new Video { Id = 2, Titulo = "Retirado ayer",  FechaBaja = new DateTime(2026, 9, 7) },
                new Video { Id = 3, Titulo = "Vivo también",   FechaBaja = null },
                new Video { Id = 4, Titulo = "Retirado hoy",   FechaBaja = new DateTime(2026, 9, 8) }
            }.AsQueryable();
        }

        [TestMethod]
        public void PorDefecto_ElListadoNoDevuelveLosVideosDeBaja()
        {
            List<Video> resultado = ServicioVideos.QuitarLasBajas(Catalogo(), incluirBajas: false).ToList();

            CollectionAssert.AreEquivalent(new[] { 1, 3 }, resultado.Select(v => v.Id).ToList(),
                "El listado por defecto ha devuelto vídeos de baja. Con el módulo 1.6.6 en " +
                "producción, eso reactiva 84 fichas dadas de baja en la tienda.");
        }

        [TestMethod]
        public void ConIncluirBajas_ElListadoDevuelveElCatalogoCompletoMarcado()
        {
            List<Video> resultado = ServicioVideos.QuitarLasBajas(Catalogo(), incluirBajas: true).ToList();

            Assert.AreEqual(4, resultado.Count, "Con incluirBajas tiene que venir el catálogo entero");
            Assert.AreEqual(2, resultado.Count(v => v.FechaBaja != null),
                "Y las bajas tienen que venir marcadas con su FechaBaja, que es lo que las distingue");
        }

        [TestMethod]
        public void ElParametroIncluirBajasDelServicioVienePorDefectoAFalse()
        {
            // Que el filtro funcione no basta: si el parámetro llegara por defecto a true, el
            // listado de siempre pasaría a incluir las bajas sin que nadie tocara el filtro.
            ParameterInfo parametro = typeof(IServicioVideos)
                .GetMethod(nameof(IServicioVideos.GetVideos), new[] { typeof(int), typeof(int), typeof(bool), typeof(bool), typeof(bool) })
                .GetParameters()
                .Single(p => p.Name == "incluirBajas");

            Assert.IsTrue(parametro.HasDefaultValue, "incluirBajas tiene que tener valor por defecto");
            Assert.AreEqual(false, parametro.DefaultValue,
                "El valor por defecto de incluirBajas TIENE que ser false: es el contrato con la tienda");
        }

        [TestMethod]
        public void ElParametroIncluirBajasDelControladorVienePorDefectoAFalse()
        {
            // El mismo contrato en la superficie HTTP: una petición sin ?incluirBajas se comporta
            // exactamente como se comportaba antes de que el parámetro existiera.
            ParameterInfo parametro = typeof(VideosController)
                .GetMethod(nameof(VideosController.GetVideos))
                .GetParameters()
                .Single(p => p.Name == "incluirBajas");

            Assert.IsTrue(parametro.HasDefaultValue, "incluirBajas tiene que tener valor por defecto");
            Assert.AreEqual(false, parametro.DefaultValue,
                "GET /api/Videos sin incluirBajas tiene que seguir devolviendo solo los vivos");
        }
    }
}
