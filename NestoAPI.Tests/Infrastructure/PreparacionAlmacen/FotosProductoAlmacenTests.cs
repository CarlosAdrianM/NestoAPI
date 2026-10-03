using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>Ariadna#2: la URL de la foto la da la API (la tienda), cacheada y sin hacer esperar al mozo.</summary>
    [TestClass]
    public class FotosProductoAlmacenTests
    {
        private DateTime ahora;
        private ConcurrentDictionary<string, int> preguntas;
        private Func<string, Task<string>> tienda;

        [TestInitialize]
        public void Preparar()
        {
            ahora = new DateTime(2026, 10, 3, 12, 0, 0);
            preguntas = new ConcurrentDictionary<string, int>();
            tienda = p => Task.FromResult("https://tienda.es/" + p + "-home_default/foto.jpg");
        }

        private FotosProductoAlmacen Fotos(CacheFotosProducto cache = null, TimeSpan? tiempoMaximo = null)
        {
            return new FotosProductoAlmacen(p =>
            {
                _ = preguntas.AddOrUpdate(p, 1, (_, n) => n + 1);
                return tienda(p);
            }, cache ?? new CacheFotosProducto(), () => ahora, tiempoMaximo ?? TimeSpan.FromSeconds(5));
        }

        [TestMethod]
        public async Task ConFoto_DevuelveLaUrlDeLaTienda()
        {
            IDictionary<string, string> urls = await Fotos().Urls(new[] { "22624 " });

            Assert.AreEqual("https://tienda.es/22624-home_default/foto.jpg", urls["22624"]);
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("https://")]
        [DataRow("https://tienda.es/-home_default/.jpg")]
        [DataRow("https://tienda.es/-")]
        public async Task RutaRota_Null(string respuesta)
        {
            tienda = _ => Task.FromResult(respuesta);

            IDictionary<string, string> urls = await Fotos().Urls(new[] { "18004" });

            Assert.IsNull(urls["18004"]);
        }

        [TestMethod]
        public async Task LaSegundaVez_SaleDeLaCache_TambienElNoTieneFoto()
        {
            tienda = p => Task.FromResult(p == "18004" ? "https://tienda.es/-home_default/.jpg" : "https://tienda.es/" + p + "/foto.jpg");
            var cache = new CacheFotosProducto();

            _ = await Fotos(cache).Urls(new[] { "22624", "18004" });
            IDictionary<string, string> urls = await Fotos(cache).Urls(new[] { "22624", "18004" });

            Assert.AreEqual(1, preguntas["22624"]);
            Assert.AreEqual(1, preguntas["18004"]);
            Assert.IsNull(urls["18004"]);
            Assert.AreEqual("https://tienda.es/22624/foto.jpg", urls["22624"]);
        }

        [TestMethod]
        public async Task PasadasLasDoceHoras_SeVuelveAPreguntar()
        {
            var cache = new CacheFotosProducto();
            _ = await Fotos(cache).Urls(new[] { "22624" });

            ahora = ahora.AddHours(12).AddMinutes(1);
            _ = await Fotos(cache).Urls(new[] { "22624" });

            Assert.AreEqual(2, preguntas["22624"]);
        }

        [TestMethod]
        public async Task SiLaTiendaFalla_SinFoto_YSeVuelveAPreguntarALosCincoMinutos()
        {
            tienda = _ => Task.FromResult<string>(null); // así contesta RutaImagen cuando no ha podido preguntar
            var cache = new CacheFotosProducto();

            IDictionary<string, string> urls = await Fotos(cache).Urls(new[] { "22624" });
            _ = await Fotos(cache).Urls(new[] { "22624" });
            ahora = ahora.AddMinutes(6);
            _ = await Fotos(cache).Urls(new[] { "22624" });

            Assert.IsNull(urls["22624"]);
            Assert.AreEqual(2, preguntas["22624"], "Una al principio, ninguna dentro de los 5 minutos y otra después");
        }

        [TestMethod]
        public async Task SiLaTiendaNoContesta_NoSeEspera()
        {
            tienda = _ => new TaskCompletionSource<string>().Task; // no contesta nunca

            Task<IDictionary<string, string>> pregunta = Fotos(tiempoMaximo: TimeSpan.FromMilliseconds(50)).Urls(new[] { "22624" });

            Assert.AreSame(pregunta, await Task.WhenAny(pregunta, Task.Delay(TimeSpan.FromSeconds(5))), "No puede colgar el picking");
            Assert.IsNull((await pregunta)["22624"]);
        }

        [TestMethod]
        public async Task ComoMuchoOchoALaVez()
        {
            int aLaVez = 0;
            int maximo = 0;
            tienda = async p =>
            {
                int ahoraMismo = Interlocked.Increment(ref aLaVez);
                InterlockedMaximo(ref maximo, ahoraMismo);
                await Task.Delay(20);
                _ = Interlocked.Decrement(ref aLaVez);
                return "https://tienda.es/" + p + "/foto.jpg";
            };
            var productos = new List<string>();
            for (int i = 0; i < 30; i++)
            {
                productos.Add("P" + i);
            }

            IDictionary<string, string> urls = await Fotos().Urls(productos);

            Assert.AreEqual(30, urls.Count);
            Assert.IsTrue(maximo <= FotosProductoAlmacen.MAXIMO_EN_PARALELO, $"Ha habido {maximo} a la vez");
            Assert.IsTrue(maximo > 1, "En paralelo, no de una en una");
        }

        private static void InterlockedMaximo(ref int destino, int valor)
        {
            int actual;
            while (valor > (actual = destino))
            {
                if (Interlocked.CompareExchange(ref destino, valor, actual) == actual)
                {
                    return;
                }
            }
        }
    

        [TestMethod]
        public async Task Olvidar_LaSiguienteVezVuelveAPreguntarALaTienda()
        {
            // Ariadna#8: al corregir la foto, el mozo la ve nueva sin esperar 12 horas
            var cache = new CacheFotosProducto();
            FotosProductoAlmacen servicio = Fotos(cache);
            _ = await servicio.Urls(new[] { "22624" });

            servicio.Olvidar("22624 ");
            _ = await servicio.Urls(new[] { "22624" });

            Assert.AreEqual(2, preguntas["22624"]);
        }

        [TestMethod]
        public async Task UrlActual_PreguntaALaTiendaAunqueEsteEnLaCacheYLaPoneAlDia()
        {
            // Ariadna#8: para saber si la foto ha cambiado no vale la caché
            var cache = new CacheFotosProducto();
            FotosProductoAlmacen servicio = Fotos(cache);
            _ = await servicio.Urls(new[] { "22624" });
            tienda = p => Task.FromResult("https://tienda.es/" + p + "-home_default/nueva.jpg");

            string actual = await servicio.UrlActual("22624");
            IDictionary<string, string> urls = await servicio.Urls(new[] { "22624" });

            Assert.AreEqual("https://tienda.es/22624-home_default/nueva.jpg", actual);
            Assert.AreEqual(actual, urls["22624"]);
            Assert.AreEqual(2, preguntas["22624"]);
        }
    }
}
