using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.SugerenciasProductos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#751: que al vendedor le compense pulsar ✓/✗: su resumen personal (MiResumen) y los puntos para los key users
    /// (Participacion, #109). Solo cuenta lo que marca: lo que entra en el pedido sin ✓ no es un «sí» ni da puntos.
    /// </summary>
    [TestClass]
    public class ResumenRespuestasSugerenciasTests
    {
        // Jueves 29/10/26: la semana empieza el lunes 26/10
        private static readonly DateTime HOY = new DateTime(2026, 10, 29);

        private static FilaRespuestaSugerencia Fila(DateTime cuando, string respuesta = null, bool? entro = null, decimal? importe = null,
            string usuario = "Paloma", string texto = "Lo compra cada ~30 días")
            => new FilaRespuestaSugerencia
            {
                Usuario = usuario,
                FechaMostrada = cuando,
                FechaRespuesta = respuesta == null ? (DateTime?)null : cuando.AddMinutes(1),
                Respuesta = respuesta,
                EntroEnPedido = entro,
                ImporteEnPedido = importe,
                Texto = texto
            };

        [TestMethod]
        public void Lunes_EsElLunesDeLaSemana()
        {
            Assert.AreEqual(new DateTime(2026, 10, 26), ResumenRespuestasSugerencias.Lunes(new DateTime(2026, 10, 26, 9, 0, 0)));
            Assert.AreEqual(new DateTime(2026, 10, 26), ResumenRespuestasSugerencias.Lunes(new DateTime(2026, 11, 1)), "El domingo es de la semana que acaba");
            Assert.AreEqual(new DateTime(2026, 10, 5), ResumenRespuestasSugerencias.InicioResumen(HOY));
        }

        [TestMethod]
        public void SinDominio_QuitaElDominioDeNesto()
        {
            Assert.AreEqual("Paloma", ResumenRespuestasSugerencias.SinDominio("NUEVAVISION\\Paloma"));
            Assert.AreEqual("Jesus", ResumenRespuestasSugerencias.SinDominio("Jesus"));
            Assert.IsNull(ResumenRespuestasSugerencias.SinDominio(null));
        }

        [TestMethod]
        public void CalcularResumen_CuentaPorSemanasYSoloLasAnadidasQueEntranSonVentas()
        {
            var filas = new List<FilaRespuestaSugerencia>
            {
                // Esta semana: dos ✓ (una entró, 120 €), una ✗, una 🚫, una sin tocar que entró (no es venta de la sugerencia)
                Fila(HOY.AddHours(10), RespuestasSugerencia.ANADIDO, true, 120m),
                Fila(HOY.AddHours(10), RespuestasSugerencia.ANADIDO, false),
                Fila(HOY.AddHours(11), RespuestasSugerencia.NO_LE_INTERESA),
                Fila(HOY.AddHours(11), RespuestasSugerencia.DESCARTADO_VENDEDOR),
                Fila(HOY.AddHours(12), null, true, 50m),
                Fila(HOY.AddHours(12), RespuestasSugerencia.SUSTITUIDA),
                // Hace dos semanas: un 🚫 provisional (antes del script Issue670b) y una ✗
                Fila(new DateTime(2026, 10, 14), RespuestasSugerencia.NO_LE_INTERESA, texto: RespuestasSugerencia.MARCA_DESCARTADO_VENDEDOR + "Va en el"),
                Fila(new DateTime(2026, 10, 14), RespuestasSugerencia.NO_LE_INTERESA),
            };

            ResumenSugerenciasVendedorDTO resumen = ResumenRespuestasSugerencias.CalcularResumen(filas, HOY);

            Assert.AreEqual(ResumenRespuestasSugerencias.SEMANAS_RESUMEN, resumen.Semanas.Count);
            Assert.AreEqual(new DateTime(2026, 10, 26), resumen.EstaSemana.Desde);
            Assert.AreSame(resumen.Semanas.Last(), resumen.EstaSemana);
            Assert.AreEqual(6, resumen.EstaSemana.Mostradas);
            Assert.AreEqual(2, resumen.EstaSemana.Anadidas);
            Assert.AreEqual(1, resumen.EstaSemana.NoLeInteresa);
            Assert.AreEqual(1, resumen.EstaSemana.Quitadas);
            Assert.AreEqual(4, resumen.EstaSemana.Respuestas, "«Sustituida» no es de nadie");
            Assert.AreEqual(1, resumen.EstaSemana.AnadidasEnPedido);
            Assert.AreEqual(120m, resumen.EstaSemana.ImporteVentas, "Lo que entra sin pulsar ✓ no cuenta");

            ResumenSemanaSugerenciasDTO del12 = resumen.Semanas.Single(s => s.Desde == new DateTime(2026, 10, 12));
            Assert.AreEqual(1, del12.Quitadas);
            Assert.AreEqual(1, del12.NoLeInteresa);
            Assert.IsNull(resumen.AceptadasDeCadaDiez, "2 ✓ y 2 ✗: con tan pocas no se dice nada");
            StringAssert.Contains(resumen.Frase, "Esta semana: 2 productos añadidos desde las sugerencias, 120,00 € en ventas.");
            StringAssert.Contains(resumen.Frase, ResumenRespuestasSugerencias.FRASE_ANIMO);
            StringAssert.Contains(resumen.Ayuda, "Semana del 12/10: 0 ✓, 1 ✗, 1 🚫");
            StringAssert.Contains(resumen.Ayuda, "Esta semana: 2 ✓ (1 en el pedido, 120,00 €), 1 ✗, 1 🚫");
        }

        [TestMethod]
        public void CalcularResumen_AceptadasDeCadaDiezConCincoOfrecidas()
        {
            var filas = new List<FilaRespuestaSugerencia>
            {
                Fila(HOY, RespuestasSugerencia.ANADIDO),
                Fila(HOY, RespuestasSugerencia.ANADIDO),
                Fila(HOY, RespuestasSugerencia.NO_LE_INTERESA),
                Fila(HOY, RespuestasSugerencia.NO_LE_INTERESA),
                Fila(HOY, RespuestasSugerencia.NO_LE_INTERESA),
                Fila(HOY, RespuestasSugerencia.DESCARTADO_VENDEDOR) // no se ofreció: no cuenta
            };

            ResumenSugerenciasVendedorDTO resumen = ResumenRespuestasSugerencias.CalcularResumen(filas, HOY);

            Assert.AreEqual(4, resumen.AceptadasDeCadaDiez);
            StringAssert.Contains(resumen.Ayuda, "De cada 10 que ha ofrecido, le han aceptado 4.");
        }

        [TestMethod]
        public void Frase_SinRespuestasSoloAnimaYSinAnadidasAgradece()
        {
            Assert.AreEqual(ResumenRespuestasSugerencias.FRASE_ANIMO,
                ResumenRespuestasSugerencias.CalcularResumen(new List<FilaRespuestaSugerencia> { Fila(HOY) }, HOY).Frase);
            Assert.AreEqual(ResumenRespuestasSugerencias.FRASE_ANIMO, ResumenRespuestasSugerencias.CalcularResumen(null, HOY).Frase);
            Assert.AreEqual("Esta semana ha contestado 1 sugerencia: gracias. " + ResumenRespuestasSugerencias.FRASE_ANIMO,
                ResumenRespuestasSugerencias.CalcularResumen(new List<FilaRespuestaSugerencia> { Fila(HOY, RespuestasSugerencia.NO_LE_INTERESA) }, HOY).Frase);
            Assert.AreEqual("Esta semana: 1 producto añadido desde las sugerencias. " + ResumenRespuestasSugerencias.FRASE_ANIMO,
                ResumenRespuestasSugerencias.CalcularResumen(new List<FilaRespuestaSugerencia> { Fila(HOY, RespuestasSugerencia.ANADIDO) }, HOY).Frase);
        }

        [TestMethod]
        public void Puntos_AnadidoQueEntraValeMasYLoNoTocadoNoVale()
        {
            Assert.AreEqual(ResumenRespuestasSugerencias.PUNTOS_ANADIDO, ResumenRespuestasSugerencias.Puntos(Fila(HOY, RespuestasSugerencia.ANADIDO, false)));
            Assert.AreEqual(ResumenRespuestasSugerencias.PUNTOS_ANADIDO + ResumenRespuestasSugerencias.PUNTOS_EXTRA_ANADIDO_EN_PEDIDO,
                ResumenRespuestasSugerencias.Puntos(Fila(HOY, RespuestasSugerencia.ANADIDO, true)));
            Assert.AreEqual(ResumenRespuestasSugerencias.PUNTOS_NO_LE_INTERESA, ResumenRespuestasSugerencias.Puntos(Fila(HOY, RespuestasSugerencia.NO_LE_INTERESA)));
            Assert.AreEqual(ResumenRespuestasSugerencias.PUNTOS_QUITADA, ResumenRespuestasSugerencias.Puntos(Fila(HOY, RespuestasSugerencia.DESCARTADO_VENDEDOR)));
            Assert.AreEqual(0, ResumenRespuestasSugerencias.Puntos(Fila(HOY, null, true)), "Entrar en el pedido sin ✓ no da puntos");
            Assert.AreEqual(0, ResumenRespuestasSugerencias.Puntos(Fila(HOY, RespuestasSugerencia.SUSTITUIDA, true)));
        }

        [TestMethod]
        public void CalcularParticipacion_JuntaNestoYNestoAppTopeDiarioYOrdenaPorPuntos()
        {
            var filas = new List<FilaRespuestaSugerencia>();
            // Paloma: 25 ✗ el mismo día (solo 20 con puntos) desde Nesto, con dominio
            for (int i = 0; i < 25; i++)
            {
                filas.Add(Fila(HOY.AddHours(9).AddMinutes(i), RespuestasSugerencia.NO_LE_INTERESA, usuario: "NUEVAVISION\\Paloma"));
            }
            // Inaki: una ✓ que entró desde Nesto y una 🚫 desde NestoApp (sin dominio), otro día una sin tocar
            filas.Add(Fila(HOY, RespuestasSugerencia.ANADIDO, true, 33.34m, "NUEVAVISION\\Inaki"));
            filas.Add(Fila(HOY.AddDays(-1), RespuestasSugerencia.DESCARTADO_VENDEDOR, usuario: "Inaki"));
            filas.Add(Fila(HOY.AddDays(-2), null, true, usuario: "Inaki"));
            // Jesus: solo enseñadas, nada marcado: no sale
            filas.Add(Fila(HOY, null, usuario: "Jesus"));

            ParticipacionSugerenciasDTO participacion = ResumenRespuestasSugerencias.CalcularParticipacion(filas, HOY.AddDays(-30), HOY.AddDays(1));

            Assert.AreEqual(2, participacion.PorUsuario.Count);
            ParticipacionSugerenciasUsuarioDTO paloma = participacion.PorUsuario[0];
            Assert.AreEqual("Paloma", paloma.Usuario);
            Assert.AreEqual(25, paloma.NoLeInteresa);
            Assert.AreEqual(5, paloma.RespuestasSinPuntos);
            Assert.AreEqual(ResumenRespuestasSugerencias.MAXIMO_RESPUESTAS_CON_PUNTOS_AL_DIA * ResumenRespuestasSugerencias.PUNTOS_NO_LE_INTERESA, paloma.Puntos);

            ParticipacionSugerenciasUsuarioDTO inaki = participacion.PorUsuario[1];
            Assert.AreEqual("Inaki", inaki.Usuario);
            Assert.AreEqual(1, inaki.Anadidas);
            Assert.AreEqual(1, inaki.Quitadas);
            Assert.AreEqual(1, inaki.AnadidasEnPedido);
            Assert.AreEqual(33.34m, inaki.ImporteVentas);
            Assert.AreEqual(ResumenRespuestasSugerencias.PUNTOS_ANADIDO + ResumenRespuestasSugerencias.PUNTOS_EXTRA_ANADIDO_EN_PEDIDO + ResumenRespuestasSugerencias.PUNTOS_QUITADA, inaki.Puntos);
            StringAssert.Contains(participacion.Reglas, "sin pulsar ✓ no da puntos");
        }

        [TestMethod]
        public async Task GetMiResumen_LeeSoloLoDeQuienLlamaDesdeHaceCuatroSemanas()
        {
            string usuarioLeido = null;
            DateTime? desdeLeido = null, hastaLeido = null;
            SugerenciasProductosController controller = Controller("NUEVAVISION\\Vendedores", "NUEVAVISION\\Paloma");
            controller.LeerFilasRespuestas = (d, h, u) =>
            {
                desdeLeido = d;
                hastaLeido = h;
                usuarioLeido = u;
                return Task.FromResult(new List<FilaRespuestaSugerencia> { Fila(HOY, RespuestasSugerencia.ANADIDO, true, 10m) });
            };

            var resultado = await controller.GetMiResumen() as OkNegotiatedContentResult<ResumenSugerenciasVendedorDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("NUEVAVISION\\Paloma", usuarioLeido);
            Assert.AreEqual(new DateTime(2026, 10, 5), desdeLeido);
            Assert.AreEqual(new DateTime(2026, 10, 30), hastaLeido);
            Assert.AreEqual(1, resultado.Content.EstaSemana.AnadidasEnPedido);
        }

        [TestMethod]
        public async Task GetMiResumen_SinUsuario_Forbidden()
        {
            SugerenciasProductosController controller = Controller("NUEVAVISION\\Vendedores", string.Empty);
            controller.LeerFilasRespuestas = (d, h, u) => Task.FromResult(new List<FilaRespuestaSugerencia>());

            var resultado = await controller.GetMiResumen() as StatusCodeResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
        }

        [TestMethod]
        public async Task GetParticipacion_SoloDireccionEInformaticaYTodosLosUsuarios()
        {
            SugerenciasProductosController vendedor = Controller("NUEVAVISION\\Vendedores", "NUEVAVISION\\Paloma");
            vendedor.LeerFilasRespuestas = (d, h, u) => Task.FromResult(new List<FilaRespuestaSugerencia>());
            var prohibido = await vendedor.GetParticipacion() as StatusCodeResult;
            Assert.IsNotNull(prohibido);
            Assert.AreEqual(HttpStatusCode.Forbidden, prohibido.StatusCode);

            string usuarioLeido = "sin leer";
            DateTime? desdeLeido = null, hastaLeido = null;
            SugerenciasProductosController direccion = Controller("NUEVAVISION\\Dirección", "NUEVAVISION\\Carlos");
            direccion.LeerFilasRespuestas = (d, h, u) =>
            {
                desdeLeido = d;
                hastaLeido = h;
                usuarioLeido = u;
                return Task.FromResult(new List<FilaRespuestaSugerencia> { Fila(HOY, RespuestasSugerencia.NO_LE_INTERESA) });
            };
            var resultado = await direccion.GetParticipacion() as OkNegotiatedContentResult<ParticipacionSugerenciasDTO>;

            Assert.IsNotNull(resultado);
            Assert.IsNull(usuarioLeido, "Todos los usuarios");
            Assert.AreEqual(new DateTime(2026, 9, 29), desdeLeido);
            Assert.AreEqual(new DateTime(2026, 10, 30), hastaLeido);
            Assert.AreEqual(1, resultado.Content.PorUsuario.Count);

            Assert.IsInstanceOfType(await direccion.GetParticipacion(new DateTime(2026, 10, 10), new DateTime(2026, 10, 9)), typeof(BadRequestErrorMessageResult));
        }

        private static SugerenciasProductosController Controller(string grupo, string usuario)
        {
            var controller = new SugerenciasProductosController(null, null, () => HOY.AddHours(10))
            {
                Request = new System.Net.Http.HttpRequestMessage(),
                Configuration = new System.Web.Http.HttpConfiguration()
            };
            controller.User = new GenericPrincipal(new GenericIdentity(usuario), new[] { grupo });
            return controller;
        }
    }
}
