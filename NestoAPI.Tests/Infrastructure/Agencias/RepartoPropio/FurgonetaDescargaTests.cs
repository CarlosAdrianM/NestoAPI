using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using NestoAPI.Models.PedidosVenta;
using FakeItEasy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>
    /// Incidencias 1189 y 1190 de Novedades (11/11/26): Miguel no pudo vaciar su furgoneta.
    /// <list type="bullet">
    /// <item>Envío 250810 (nota de ENTREGA 930404, sin etiqueta RP: solo el papel NE930404). Salió sin cargar, se puso «Ausente» y al
    /// volver lo «cargó» con el papel solo para poder descargarlo; Descargar no admitía el NE.</item>
    /// <item>Envío 250806 (nota de RECOGIDA 930317): al cargar la parada quedó «RP250806-1 Cargado» (en una recogida no hay bulto
    /// hasta recogerlo). Se recogió y descargó la NR, pero el RP se quedó para siempre en la furgoneta, sin nada que leer.</item>
    /// </list>
    /// </summary>
    public partial class FurgonetaBultosTests
    {
        private const int NOTA_ENTREGA = 930404;
        private const int ENVIO_NOTA_ENTREGA = 251600;

        private EnviosAgencia ConNotaEntrega(short bultos = 1)
        {
            EnviosAgencia envio = Envio(ENVIO_NOTA_ENTREGA, NOTA_ENTREGA, bultos);
            notas.Filas.Add(new NotaParadaReparto { Pedido = NOTA_ENTREGA, Tipo = TiposNotaEntrega.ENTREGA, NumeroNota = 5601 });
            return envio;
        }

        private static MovimientoBultoRepartoDTO SinLeer(string codigo, Guid? clave = null)
            => new MovimientoBultoRepartoDTO { Codigo = codigo, ClaveIdempotencia = clave ?? Guid.NewGuid(), SinLeer = true };

        private MovimientoBultoReparto Fila(string codigo, string tipo, string usuario, DateTime fechaHora, int? envio, int? pedido = null)
        {
            MovimientoBultoReparto m = Mov(furgoneta.Filas.Count + 1, codigo, tipo, usuario, fechaHora);
            m.EnvioId = envio;
            m.Pedido = pedido;
            m.Total = 1;
            furgoneta.Filas.Add(m);
            return m;
        }

        // ================================================================ C: el «Cargado» RP de una recogida

        [TestMethod]
        public async Task Cargar_LaParadaDeUnaRecogidaLeyendoSuRP_NoApuntaCargado_PeroLaParadaEsSuya()
        {
            ConNotaRecogida();

            var r = await servicio.Cargar(Carga("RP251501-1"), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(1, cargas.Filas.Count, "La carga de la parada (quién la lleva) sigue apuntándose");
            Assert.AreEqual(0, furgoneta.Filas.Count, "En una recogida no hay bulto que cargar: nada en la furgoneta");
            Assert.AreEqual(0, (await LoQueLleva(MIGUEL)).Count);
            EnvioRepartoDTO parada = (await servicio.Envios(AHORA, true, MIGUEL)).Contenido.Single(e => e.EnvioId == 251501);
            Assert.AreEqual("Miguel", parada.Repartidor, "Sigue siendo suya (soloMios)");
            Assert.AreEqual(1, parada.BultosCargados);
        }

        [TestMethod]
        public async Task Cargar_LaParadaDeUnaRecogidaConElPapelNE_TampocoApuntaCargado()
        {
            ConNotaRecogida();

            var r = await servicio.Cargar(Carga($"NE{NOTA_RECOGIDA}"), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(0, furgoneta.Filas.Count);
        }

        [TestMethod]
        public async Task Cargar_UnaNotaDeEntregaConSuPapelNE_SiApuntaElCargadoDeSuEnvio()
        {
            ConNotaEntrega();

            _ = await servicio.Cargar(Carga($"NE{NOTA_ENTREGA}"), MIGUEL);

            Assert.AreEqual($"RP{ENVIO_NOTA_ENTREGA}-1", furgoneta.Filas.Single().Codigo);
        }

        // ================================================================ B: la NR cierra el RP fantasma de su parada

        [TestMethod]
        public async Task Descargar_LaNR_CierraTambienElRPFantasmaDeSuParada()
        {
            ConNotaRecogida();
            Fila("RP251501-1", TiposMovimientoBulto.CARGADO, MIGUEL, AHORA, 251501);
            _ = await servicio.Recoger(Lectura($"NR{NOTA_RECOGIDA}-1", null, AHORA.AddHours(1)), MIGUEL);

            var r = await servicio.Descargar(Lectura($"NR{NOTA_RECOGIDA}-1", null, AHORA.AddHours(2)), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.IsFalse(r.Contenido.YaEstaba);
            CollectionAssert.AreEquivalent(new[] { $"NR{NOTA_RECOGIDA}-1", "RP251501-1" }, r.Contenido.Descargados);
            StringAssert.Contains(r.Contenido.Mensaje, "RP251501-1");
            Assert.AreEqual(0, (await LoQueLleva(MIGUEL)).Count);
        }

        [TestMethod]
        public async Task Descargar_LaNRYaDescargada_PeroConElRPFantasma_DejaLaFurgonetaLimpia()
        {
            // El caso real del envío 250806: RP Cargado 10:19, NR Recogido 12:00, NR Descargado 13:02… y el RP, encima.
            ConNotaRecogida();
            Fila("RP251501-1", TiposMovimientoBulto.CARGADO, MIGUEL, AHORA, 251501);
            Fila($"NR{NOTA_RECOGIDA}-1", TiposMovimientoBulto.RECOGIDO, MIGUEL, AHORA.AddHours(2), 251501, NOTA_RECOGIDA);
            Fila($"NR{NOTA_RECOGIDA}-1", TiposMovimientoBulto.DESCARGADO, MIGUEL, AHORA.AddHours(3), 251501, NOTA_RECOGIDA);
            Assert.AreEqual(1, (await LoQueLleva(MIGUEL)).Count);

            var r = await servicio.Descargar(Lectura($"NR{NOTA_RECOGIDA}"), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.IsFalse(r.Contenido.YaEstaba, "Ha salido algo de la furgoneta");
            CollectionAssert.AreEqual(new[] { "RP251501-1" }, r.Contenido.Descargados);
            Assert.AreEqual(0, (await LoQueLleva(MIGUEL)).Count);
            Assert.AreEqual(0, r.Contenido.EnFurgoneta);
        }

        [TestMethod]
        public async Task Descargar_LaNR_SinRPFantasma_ComoSiempre()
        {
            ConNotaRecogida();
            _ = await servicio.Recoger(Lectura($"NR{NOTA_RECOGIDA}-1"), MIGUEL);

            var r = await servicio.Descargar(Lectura($"NR{NOTA_RECOGIDA}-1"), MIGUEL);
            var otraVez = await servicio.Descargar(Lectura($"NR{NOTA_RECOGIDA}-1"), MIGUEL);

            Assert.AreEqual($"Bulto NR{NOTA_RECOGIDA}-1 descargado.", r.Contenido.Mensaje);
            CollectionAssert.AreEqual(new[] { $"NR{NOTA_RECOGIDA}-1" }, r.Contenido.Descargados);
            Assert.IsTrue(otraVez.Contenido.YaEstaba);
            Assert.AreEqual(0, otraVez.Contenido.Descargados.Count);
        }

        // ================================================================ A: Descargar con el papel NE{pedido}

        [TestMethod]
        public async Task Descargar_ConElPapelNE_DescargaLosBultosDeSuEnvio()
        {
            ConNotaEntrega(bultos: 2);
            _ = await servicio.Cargar(Carga($"RP{ENVIO_NOTA_ENTREGA}-1"), MIGUEL);
            _ = await servicio.Cargar(Carga($"RP{ENVIO_NOTA_ENTREGA}-2"), MIGUEL);
            Guid clave = Guid.NewGuid();

            var r = await servicio.Descargar(Lectura($"ne{NOTA_ENTREGA} ", clave), MIGUEL);
            int filas = furgoneta.Filas.Count;
            var reintento = await servicio.Descargar(Lectura($"NE{NOTA_ENTREGA}", clave), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            CollectionAssert.AreEqual(new[] { $"RP{ENVIO_NOTA_ENTREGA}-1", $"RP{ENVIO_NOTA_ENTREGA}-2" }, r.Contenido.Descargados);
            Assert.AreEqual(TiposMovimientoBulto.DESCARGADO, r.Contenido.Tipo);
            Assert.AreEqual(ENVIO_NOTA_ENTREGA, r.Contenido.EnvioId);
            Assert.AreEqual(NOTA_ENTREGA, r.Contenido.Pedido);
            StringAssert.Contains(r.Contenido.Mensaje, "2 bultos");
            Assert.AreEqual(0, (await LoQueLleva(MIGUEL)).Count);
            Assert.AreEqual(TipoResultadoReparto.Ok, reintento.Tipo, reintento.Mensaje);
            Assert.AreEqual(filas, furgoneta.Filas.Count, "Misma clave: no se apunta otra vez");
        }

        [TestMethod]
        public async Task Descargar_ConElPapelNE_DeUnEnvioQueNoEstaEnNingunaFurgoneta_409QueDiceQueHacer()
        {
            ConNotaEntrega();

            var r = await servicio.Descargar(Lectura($"NE{NOTA_ENTREGA}"), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Conflicto, r.Tipo);
            Assert.AreEqual(ServicioRepartoPropio.CODIGO_NO_EN_FURGONETA, r.Codigo);
            StringAssert.Contains(r.Mensaje, $"NE{NOTA_ENTREGA}");
            StringAssert.Contains(r.Mensaje, "no figura en ninguna furgoneta");
            StringAssert.Contains(r.Mensaje, "no tiene que descargarla");
            Assert.IsFalse(r.Mensaje.Contains("no es de un bulto"), "No es un «código no válido»");
        }

        [TestMethod]
        public async Task Descargar_ConElPapelNE_YaDescargado_200YaEstaba()
        {
            ConNotaEntrega();
            _ = await servicio.Cargar(Carga($"NE{NOTA_ENTREGA}"), MIGUEL);
            _ = await servicio.Descargar(Lectura($"RP{ENVIO_NOTA_ENTREGA}-1"), MIGUEL);

            var r = await servicio.Descargar(Lectura($"NE{NOTA_ENTREGA}"), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.IsTrue(r.Contenido.YaEstaba);
            StringAssert.Contains(r.Contenido.Mensaje, "ya estaba descargad");
        }

        [TestMethod]
        public async Task Descargar_UnNEQueNoEsNota_404()
        {
            var r = await servicio.Descargar(Lectura("NE111111"), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.NoEncontrado, r.Tipo);
            StringAssert.Contains(r.Mensaje, "NE111111");
        }

        // ================================================================ la incidencia de un envío que salió sin cargar

        private static IncidenciaRepartoDTO Ausente(Guid? clave = null)
            => new IncidenciaRepartoDTO { ClaveIdempotencia = clave ?? Guid.NewGuid(), Motivo = IncidenciaRepartoDTO.AUSENTE, FechaHora = AHORA.AddHours(3) };

        [TestMethod]
        public async Task Incidencia_DeUnEnvioQueSalioSinCargar_LoApuntaEnSuFurgoneta_YSeDescargaConElPapel()
        {
            // El caso real del envío 250810: sin «Cargado», «Ausente» a las 12:14; al volver no había forma de descargarlo
            ConNotaEntrega();
            Guid clave = Guid.NewGuid();

            var incidencia = await servicio.Incidencia(ENVIO_NOTA_ENTREGA, Ausente(clave), MIGUEL);
            _ = await servicio.Incidencia(ENVIO_NOTA_ENTREGA, Ausente(clave), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, incidencia.Tipo, incidencia.Mensaje);
            BultoFurgonetaDTO lleva = (await LoQueLleva(MIGUEL)).Single();
            Assert.AreEqual($"RP{ENVIO_NOTA_ENTREGA}-1", lleva.Codigo);
            Assert.AreEqual(TiposMovimientoBulto.CARGADO, lleva.Tipo);
            Assert.AreEqual(AHORA.AddHours(3), lleva.FechaHora, "Con la hora de la incidencia");
            Assert.AreEqual(1, furgoneta.Filas.Count, "Reintento con la misma clave: no se apunta dos veces");

            ahora = AHORA.AddHours(4); // al volver por la tarde
            var r =await servicio.Descargar(Lectura($"NE{NOTA_ENTREGA}"), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(0, (await LoQueLleva(MIGUEL)).Count);
        }

        [TestMethod]
        public async Task Incidencia_DeUnEnvioCargado_NoApuntaNadaMas()
        {
            Envio();
            _ = await servicio.Cargar(Carga("RP251300-1"), MIGUEL);
            _ = await servicio.Cargar(Carga("RP251300-2"), MIGUEL);
            _ = await servicio.Cargar(Carga("RP251300-3"), MIGUEL);

            _ = await servicio.Incidencia(251300, Ausente(), MIGUEL);

            Assert.AreEqual(3, furgoneta.Filas.Count);
        }

        [TestMethod]
        public async Task Incidencia_DeUnEnvioQueLlevaOtro_NoSeLoQuita()
        {
            Envio(bultos: 1);
            _ = await servicio.Cargar(Carga("RP251300-1"), ANDRE);

            _ = await servicio.Incidencia(251300, Ausente(), MIGUEL);

            Assert.AreEqual(1, furgoneta.Filas.Count);
            Assert.AreEqual(1, (await LoQueLleva(ANDRE)).Count);
        }

        [TestMethod]
        public async Task Incidencia_DeUnaRecogida_NoApuntaNingunRP()
        {
            ConNotaRecogida();

            var r = await servicio.Incidencia(251501, new IncidenciaRepartoDTO { ClaveIdempotencia = Guid.NewGuid(), Motivo = IncidenciaRepartoDTO.NADA_QUE_RECOGER }, MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(0, furgoneta.Filas.Count);
        }

        // ================================================================ D: quitar sin leer (SinLeer)

        [TestMethod]
        public async Task Descargar_SinLeer_UnBultoDeSuFurgoneta_SaleConRastroYAvisoAAlmacen()
        {
            ConNotaRecogida();
            Fila("RP251501-1", TiposMovimientoBulto.CARGADO, MIGUEL, AHORA, 251501).Descripcion = "GUARDIA ROSAS (MADRID)";
            Guid clave = Guid.NewGuid();

            var r = await servicio.Descargar(SinLeer("RP251501-1", clave), MIGUEL);
            var reintento = await servicio.Descargar(SinLeer("RP251501-1", clave), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.IsTrue(r.Contenido.SinLeer);
            CollectionAssert.AreEqual(new[] { "RP251501-1" }, r.Contenido.Descargados);
            StringAssert.Contains(r.Contenido.Mensaje, "sin leerlo");
            Assert.AreEqual(0, (await LoQueLleva(MIGUEL)).Count);
            (string Tabla, string Anterior, string Nuevo, string Usuario) rastro = furgoneta.Rastros.Single();
            Assert.AreEqual(ServicioRepartoPropio.TABLA_MODIFICACIONES_SIN_LEER, rastro.Tabla);
            StringAssert.Contains(rastro.Anterior, "RP251501-1");
            StringAssert.Contains(rastro.Nuevo, "\"SinLeer\":true");
            Assert.AreEqual(MIGUEL, rastro.Usuario);
            Assert.AreEqual("RP251501-1", avisosFurgoneta.SinLeer.Single().Bulto.Codigo);
            Assert.AreEqual(TipoResultadoReparto.Ok, reintento.Tipo, reintento.Mensaje);
            Assert.AreEqual(1, avisosFurgoneta.SinLeer.Count, "Misma clave: ni otro rastro ni otro aviso");
        }

        [TestMethod]
        public async Task Descargar_SinLeer_UnBultoDeOtraFurgoneta_409SinTocarNada()
        {
            Envio();
            _ = await servicio.Cargar(Carga("RP251300-1"), ANDRE);
            int filas = furgoneta.Filas.Count;

            var r = await servicio.Descargar(SinLeer("RP251300-1"), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Conflicto, r.Tipo);
            Assert.AreEqual(ServicioRepartoPropio.CODIGO_EN_OTRA_FURGONETA, r.Codigo);
            StringAssert.Contains(r.Mensaje, "Andre");
            Assert.AreEqual(filas, furgoneta.Filas.Count);
            Assert.AreEqual(0, furgoneta.Rastros.Count);
            Assert.AreEqual(0, avisosFurgoneta.SinLeer.Count);
        }

        [TestMethod]
        public async Task Descargar_SinLeer_LoQueNoEsUnBultoDeLaLista_400()
        {
            ConNotaEntrega();
            _ = await servicio.Cargar(Carga($"NE{NOTA_ENTREGA}"), MIGUEL);

            var r = await servicio.Descargar(SinLeer($"NE{NOTA_ENTREGA}"), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Invalido, r.Tipo);
            StringAssert.Contains(r.Mensaje, "GET");
            Assert.AreEqual(1, (await LoQueLleva(MIGUEL)).Count);
        }

        [TestMethod]
        public async Task Descargar_SinLeer_LoQueNoEstaEnNinguna_409NoEnFurgoneta()
        {
            var r = await servicio.Descargar(SinLeer("RP251300-1"), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Conflicto, r.Tipo);
            Assert.AreEqual(ServicioRepartoPropio.CODIGO_NO_EN_FURGONETA, r.Codigo);
            Assert.AreEqual(0, avisosFurgoneta.SinLeer.Count);
        }

        [TestMethod]
        public async Task Descargar_SinElCampoSinLeer_ElBultoDeOtro_SeDescargaComoSiempre()
        {
            Envio();
            _ = await servicio.Cargar(Carga("RP251300-1"), ANDRE);

            var r = await servicio.Descargar(Lectura("RP251300-1"), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo);
            Assert.IsFalse(r.Contenido.SinLeer);
            Assert.AreEqual(0, furgoneta.Rastros.Count);
            Assert.AreEqual(0, avisosFurgoneta.SinLeer.Count);
        }

        [TestMethod]
        public async Task Controlador_Descargar_SinLeerDeOtro_409ConCodigo()
        {
            Envio();
            _ = await servicio.Cargar(Carga("RP251300-1"), ANDRE);
            var controlador = new RepartoPropioController(null, servicio)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Name, MIGUEL), new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.ALMACEN)
                }, "prueba"))
            };

            HttpResponseMessage respuesta = await (await controlador.Descargar(SinLeer("RP251300-1"))).ExecuteAsync(CancellationToken.None);

            Assert.AreEqual(HttpStatusCode.Conflict, respuesta.StatusCode);
            StringAssert.Contains(await respuesta.Content.ReadAsStringAsync(), "\"code\":\"EN_OTRA_FURGONETA\"");
        }

        [TestMethod]
        public async Task AvisosFurgoneta_SinLeer_SoloAlmacen_ConElBultoYElCliente()
        {
            var push = A.Fake<IServicioNotificacionesPush>();
            var guardados = new List<(string Usuario, NotificacionPushDTO Aviso)>();
            A.CallTo(() => push.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._))
                .Invokes((string u, string app, NotificacionPushDTO n) => guardados.Add((u, n))).Returns(Task.CompletedTask);
            var miembros = new Dictionary<string, List<string>>
            {
                [Constantes.GruposSeguridad.ALMACEN] = new List<string> { "Alfredo", "alfredo", "Andre" },
                [Constantes.GruposSeguridad.DIRECCION] = new List<string> { "Carlos" }
            };
            var avisador = new AvisosFurgonetaSinDescargar(() => push, g => miembros[g]);
            MovimientoBultoReparto bulto = Mov(1, "RP250806-1", TiposMovimientoBulto.DESCARGADO, MIGUEL, AHORA);
            bulto.Descripcion = "GUARDIA ROSAS LIZ JULIANA (MADRID)";

            List<string> avisados = await avisador.BultoQuitadoSinLeer(MIGUEL, bulto);

            CollectionAssert.AreEqual(new[] { "Almacén" }, avisados);
            CollectionAssert.AreEquivalent(new[] { "NUEVAVISION\\Alfredo", "NUEVAVISION\\Andre" }, guardados.Select(g => g.Usuario).ToList());
            Assert.AreEqual("Miguel ha quitado de su furgoneta sin leerlo el bulto RP250806-1 (GUARDIA ROSAS LIZ JULIANA (MADRID)). Compruebe que está en el almacén.",
                guardados[0].Aviso.Cuerpo);
            Assert.AreEqual(AvisosFurgonetaSinDescargar.TIPO_NOTIFICACION_SIN_LEER, guardados[0].Aviso.Tipo);
        }
    }
}
