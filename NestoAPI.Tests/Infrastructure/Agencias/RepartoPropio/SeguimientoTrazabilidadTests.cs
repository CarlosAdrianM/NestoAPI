using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#771: la página pública de seguimiento con la trazabilidad completa de ESTE envío (pedido → picking → embalaje →
    /// furgoneta → entrega), los otros envíos del pedido plegados y la galería de fotos del packing. Nunca nombres de empleados,
    /// DNI, firma ni GPS.
    /// </summary>
    [TestClass]
    public class SeguimientoTrazabilidadTests
    {
        private const string CLAVE = "clave-de-pruebas-del-seguimiento-reparto-propio-771";
        private const string CLAVE_FOTOS = "clave-de-pruebas-de-los-enlaces-de-las-fotos-771";
        private const int ENVIO = 250434;
        private const int ENVIO_ANTERIOR = 250314;
        private const int ENVIO_AGENCIA = 250200;
        private const int PEDIDO = 929473;
        private static readonly Guid ID_FOTO = new Guid("11111111-2222-3333-4444-555555555555");
        private Func<string> claveOriginal;
        private Func<string> urlOriginal;

        [TestInitialize]
        public void Preparar()
        {
            claveOriginal = EnlaceSeguimientoRepartoPropio.LeerClave;
            urlOriginal = EnlaceSeguimientoRepartoPropio.LeerUrlBase;
            EnlaceSeguimientoRepartoPropio.LeerClave = () => CLAVE;
            EnlaceSeguimientoRepartoPropio.LeerUrlBase = () => "https://api.nuevavision.es";
        }

        [TestCleanup]
        public void Restaurar()
        {
            EnlaceSeguimientoRepartoPropio.LeerClave = claveOriginal;
            EnlaceSeguimientoRepartoPropio.LeerUrlBase = urlOriginal;
        }

        private static EnviosAgencia Envio(int numero, short estado, DateTime fecha) => new EnviosAgencia
        {
            Numero = numero,
            Empresa = "1  ",
            Agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO,
            Pedido = PEDIDO,
            Bultos = 2,
            Estado = estado,
            Fecha = fecha
        };

        private static EnvioDelPedidoPublico DelPedido(int numero, DateTime fecha, short estado = Constantes.Agencias.ESTADO_TRAMITADO,
            int agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO, DateTime? entrega = null)
            => new EnvioDelPedidoPublico { Numero = numero, Agencia = agencia, Estado = estado, Fecha = fecha, FechaEntrega = entrega };

        /// <summary>
        /// El pedido 929473 de verdad (28/10 y 30/10): primer envío con los pickings 99951 y 99958 del 28; segundo, con el 99982 del
        /// 30. Más uno de agencia, antiguo, y un picking del 02/11 de un envío que aún no existe.
        /// </summary>
        private static DatosTrazabilidadEnvio DosEnvios()
            => new DatosTrazabilidadEnvio
            {
                FechaPedido = new DateTime(2026, 10, 27, 18, 2, 0),
                Envios = new List<EnvioDelPedidoPublico>
                {
                    DelPedido(ENVIO_ANTERIOR, new DateTime(2026, 10, 28), Constantes.Agencias.ESTADO_ENTREGADO, entrega: new DateTime(2026, 10, 29, 11, 30, 0)),
                    DelPedido(ENVIO, new DateTime(2026, 10, 30)),
                    DelPedido(ENVIO_AGENCIA, new DateTime(2026, 10, 20), Constantes.Agencias.ESTADO_TRAMITADO, agencia: Constantes.Agencias.AGENCIA_CTT)
                },
                Pickings = new List<PickingDelPedidoPublico>
                {
                    new PickingDelPedidoPublico { Picking = 99951, InicioPicking = new DateTime(2026, 10, 28, 8, 3, 49) },
                    new PickingDelPedidoPublico { Picking = 99958, InicioPicking = new DateTime(2026, 10, 28, 10, 55, 59) },
                    new PickingDelPedidoPublico { Picking = 99982, InicioPicking = new DateTime(2026, 10, 30, 13, 14, 10) },
                    new PickingDelPedidoPublico { Picking = 99999, InicioPicking = new DateTime(2026, 11, 2, 9, 0, 0) }
                },
                Bultos = new List<BultoTrazabilidad>
                {
                    new BultoTrazabilidad { Id = 40, IdCliente = Guid.NewGuid(), Picking = 99951, Fecha = new DateTime(2026, 10, 28, 9, 0, 0), TieneFoto = true },
                    new BultoTrazabilidad { Id = 60, IdCliente = Guid.NewGuid(), Picking = 99982, Fecha = new DateTime(2026, 10, 30, 13, 40, 0), TieneFoto = false },
                    new BultoTrazabilidad { Id = 61, IdCliente = Guid.NewGuid(), Picking = 99982, Fecha = new DateTime(2026, 10, 30, 13, 45, 0), TieneFoto = true }
                },
                FotosPacking = new List<FotoPackingTrazabilidad>
                {
                    new FotoPackingTrazabilidad { Id = 7, IdCliente = ID_FOTO, Picking = 99982, Fecha = new DateTime(2026, 10, 30, 13, 35, 0) },
                    new FotoPackingTrazabilidad { Id = 8, IdCliente = Guid.NewGuid(), Picking = 99951, Fecha = new DateTime(2026, 10, 28, 8, 50, 0) }
                },
                PrimeraCarga = new DateTime(2026, 10, 30, 16, 5, 0)
            };

        private static async Task<PaginaSeguimientoReparto> Pagina(EnviosAgencia envio, DatosTrazabilidadEnvio datos, List<HitoEntregaPublico> hitos = null)
        {
            var lector = A.Fake<ILectorSeguimientoPublicoReparto>();
            _ = A.CallTo(() => lector.LeerEnvio(envio.Numero)).Returns(Task.FromResult(envio));
            _ = A.CallTo(() => lector.LeerHitos(envio.Numero)).Returns(Task.FromResult(hitos ?? new List<HitoEntregaPublico>()));
            _ = A.CallTo(() => lector.LeerTrazabilidad(envio)).Returns(Task.FromResult(datos));
            return await new ServicioSeguimientoPublicoReparto(lector, claveFotos: () => CLAVE_FOTOS).Pagina(EnlaceSeguimientoRepartoPropio.Token(envio.Numero));
        }

        // ===================================================================== qué picking es de qué envío

        [TestMethod]
        public void PickingsDelEnvio_CadaEnvioLosDeSuDia_YElFuturoDeNinguno()
        {
            DatosTrazabilidadEnvio datos = DosEnvios();

            CollectionAssert.AreEquivalent(new[] { 99951, 99958 }, TrazabilidadEnvio.PickingsDelEnvio(ENVIO_ANTERIOR, datos).ToList());
            CollectionAssert.AreEquivalent(new[] { 99982 }, TrazabilidadEnvio.PickingsDelEnvio(ENVIO, datos).ToList(), "el 99999 (02/11) es de un envío que aún no existe");
        }

        [TestMethod]
        public void PickingsDelEnvio_SinEscaneos_LaFechaEsLaDeLaCajaOLaFoto()
        {
            var datos = new DatosTrazabilidadEnvio
            {
                Envios = { DelPedido(1, new DateTime(2026, 10, 28)), DelPedido(2, new DateTime(2026, 10, 30)) },
                Pickings = { new PickingDelPedidoPublico { Picking = 10 }, new PickingDelPedidoPublico { Picking = 11 } },
                Bultos = { new BultoTrazabilidad { Picking = 10, Fecha = new DateTime(2026, 10, 27, 17, 0, 0) } },
                FotosPacking = { new FotoPackingTrazabilidad { Picking = 11, Fecha = new DateTime(2026, 10, 29, 12, 0, 0) } }
            };

            CollectionAssert.AreEquivalent(new[] { 10 }, TrazabilidadEnvio.PickingsDelEnvio(1, datos).ToList());
            CollectionAssert.AreEquivalent(new[] { 11 }, TrazabilidadEnvio.PickingsDelEnvio(2, datos).ToList());
        }

        [TestMethod]
        public void PickingsDelEnvio_DosEnviosElMismoDia_ConDosPickings_UnoParaCadaUno()
        {
            var datos = new DatosTrazabilidadEnvio
            {
                Envios = { DelPedido(5, new DateTime(2026, 10, 30)), DelPedido(6, new DateTime(2026, 10, 30)) },
                Pickings =
                {
                    new PickingDelPedidoPublico { Picking = 20, InicioPicking = new DateTime(2026, 10, 30, 8, 0, 0) },
                    new PickingDelPedidoPublico { Picking = 21, InicioPicking = new DateTime(2026, 10, 30, 12, 0, 0) }
                }
            };

            CollectionAssert.AreEquivalent(new[] { 20 }, TrazabilidadEnvio.PickingsDelEnvio(5, datos).ToList());
            CollectionAssert.AreEquivalent(new[] { 21 }, TrazabilidadEnvio.PickingsDelEnvio(6, datos).ToList());
        }

        [TestMethod]
        public void PickingsDelEnvio_DosEnviosElMismoDia_ConUnPicking_EsDeLosDos()
        {
            // Casi siempre es la misma salida etiquetada dos veces
            var datos = new DatosTrazabilidadEnvio
            {
                Envios = { DelPedido(5, new DateTime(2026, 10, 30)), DelPedido(6, new DateTime(2026, 10, 30)) },
                Pickings = { new PickingDelPedidoPublico { Picking = 20, InicioPicking = new DateTime(2026, 10, 30, 8, 0, 0) } }
            };

            CollectionAssert.AreEquivalent(new[] { 20 }, TrazabilidadEnvio.PickingsDelEnvio(5, datos).ToList());
            CollectionAssert.AreEquivalent(new[] { 20 }, TrazabilidadEnvio.PickingsDelEnvio(6, datos).ToList());
        }

        [TestMethod]
        public void PickingsDelEnvio_SinFechaNiEnvio_Ninguno()
        {
            var datos = new DatosTrazabilidadEnvio
            {
                Envios = { DelPedido(5, new DateTime(2026, 10, 30)) },
                Pickings = { new PickingDelPedidoPublico { Picking = 20 } }
            };

            Assert.AreEqual(0, TrazabilidadEnvio.PickingsDelEnvio(5, datos).Count, "un picking en papel no tiene nada que enseñar");
            Assert.AreEqual(0, TrazabilidadEnvio.PickingsDelEnvio(5, null).Count);
            Assert.AreEqual(0, TrazabilidadEnvio.PickingsDelEnvio(99, DosEnvios()).Count, "un envío que no es del pedido");
        }

        // ===================================================================== hitos

        [TestMethod]
        public void HitosPrevios_ConTodo_EnOrdenYSoloDeEsteEnvio()
        {
            List<EventoSeguimientoPublico> hitos = TrazabilidadEnvio.HitosPrevios(Envio(ENVIO, Constantes.Agencias.ESTADO_TRAMITADO, new DateTime(2026, 10, 30)), DosEnvios(), false);

            CollectionAssert.AreEqual(new[] { TrazabilidadEnvio.PEDIDO_REALIZADO, TrazabilidadEnvio.PREPARANDO, TrazabilidadEnvio.EMBALADO, TrazabilidadEnvio.EN_EL_VEHICULO },
                hitos.Select(h => h.Titulo).ToList());
            Assert.AreEqual(new DateTime(2026, 10, 27, 18, 2, 0), hitos[0].Fecha);
            Assert.AreEqual(new DateTime(2026, 10, 30, 13, 14, 10), hitos[1].Fecha, "el primer escaneo del picking de ESTE envío, no el del 28");
            Assert.AreEqual(new DateTime(2026, 10, 30, 13, 45, 0), hitos[2].Fecha, "la última caja o foto de este envío");
            Assert.AreEqual(new DateTime(2026, 10, 30, 16, 5, 0), hitos[3].Fecha);
            Assert.IsTrue(hitos.All(h => h.ConHora));
        }

        [TestMethod]
        public void HitosPrevios_SinDatos_SeOmiten_YSinEmbalajeNiCarga_PreparadoParaElReparto()
        {
            EnviosAgencia envio = Envio(ENVIO, Constantes.Agencias.ESTADO_TRAMITADO, new DateTime(2026, 10, 30));

            List<EventoSeguimientoPublico> sinNada = TrazabilidadEnvio.HitosPrevios(envio, new DatosTrazabilidadEnvio(), false);
            CollectionAssert.AreEqual(new[] { TrazabilidadEnvio.PREPARADO_PARA_EL_REPARTO }, sinNada.Select(h => h.Titulo).ToList());
            Assert.IsFalse(sinNada[0].ConHora);

            List<EventoSeguimientoPublico> soloPedido = TrazabilidadEnvio.HitosPrevios(envio,
                new DatosTrazabilidadEnvio { FechaPedido = new DateTime(2026, 10, 27), Envios = { DelPedido(ENVIO, new DateTime(2026, 10, 30)) } }, false);
            CollectionAssert.AreEqual(new[] { TrazabilidadEnvio.PEDIDO_REALIZADO, TrazabilidadEnvio.PREPARADO_PARA_EL_REPARTO }, soloPedido.Select(h => h.Titulo).ToList());
            Assert.IsFalse(soloPedido[0].ConHora, "la fecha del pedido sin hora no se inventa una");

            List<EventoSeguimientoPublico> pendiente = TrazabilidadEnvio.HitosPrevios(Envio(ENVIO, (short)Constantes.Agencias.ESTADO_EN_CURSO, new DateTime(2026, 10, 30)),
                new DatosTrazabilidadEnvio(), false);
            Assert.AreEqual(0, pendiente.Count, "sin salir a reparto, nada");
        }

        [TestMethod]
        public void HitosPrevios_Recogida_RecogidaSolicitada()
        {
            List<EventoSeguimientoPublico> hitos = TrazabilidadEnvio.HitosPrevios(Envio(ENVIO, Constantes.Agencias.ESTADO_TRAMITADO, new DateTime(2026, 10, 30)),
                new DatosTrazabilidadEnvio { FechaPedido = new DateTime(2026, 10, 29, 9, 0, 0) }, true);

            CollectionAssert.AreEqual(new[] { TrazabilidadEnvio.RECOGIDA_SOLICITADA, "Recogida programada" }, hitos.Select(h => h.Titulo).ToList());
        }

        [TestMethod]
        public void Construir_LineaDeTiempo_DelPedidoALaEntrega_AunqueElRelojDeLaPdaVayaMal()
        {
            DatosTrazabilidadEnvio datos = DosEnvios();
            datos.PrimeraCarga = new DateTime(2026, 10, 30, 12, 0, 0); // la PDA del repartidor iba atrasada: «antes» del embalaje
            var hitos = new List<HitoEntregaPublico>
            {
                new HitoEntregaPublico { Tipo = "Incidencia", FechaHora = new DateTime(2026, 10, 30, 17, 0, 0), Motivo = IncidenciaRepartoDTO.AUSENTE },
                new HitoEntregaPublico { Tipo = "Entrega", FechaHora = new DateTime(2026, 10, 31, 10, 0, 0), Receptor = "María" }
            };

            VistaSeguimientoReparto vista = VistaSeguimientoReparto.Construir(Envio(ENVIO, Constantes.Agencias.ESTADO_ENTREGADO, new DateTime(2026, 10, 30)), hitos, false, datos);

            CollectionAssert.AreEqual(new[] { TrazabilidadEnvio.PEDIDO_REALIZADO, TrazabilidadEnvio.PREPARANDO, TrazabilidadEnvio.EMBALADO, TrazabilidadEnvio.EN_EL_VEHICULO, "Incidencia", "Entregado" },
                vista.Eventos.Select(e => e.Titulo).ToList());
        }

        // ===================================================================== galería

        [TestMethod]
        public void Galeria_SoloLasDeEsteEnvio_DeCajasYLibres_EnOrden()
        {
            List<FotoDeLaGaleria> fotos = TrazabilidadEnvio.Galeria(Envio(ENVIO, Constantes.Agencias.ESTADO_TRAMITADO, new DateTime(2026, 10, 30)), DosEnvios());

            CollectionAssert.AreEqual(new[] { 7, 61 }, fotos.Select(f => f.Id).ToList(), "la libre de las 13:35 y la de la caja de las 13:45; ni las del 28 ni la caja sin foto");
            Assert.IsTrue(fotos[0].EsDelPacking);
            Assert.IsFalse(fotos[1].EsDelPacking);
        }

        [TestMethod]
        public async Task Pagina_ConFotos_GaleriaConEnlacesFirmados_QueSeAmplian()
        {
            PaginaSeguimientoReparto pagina = await Pagina(Envio(ENVIO, Constantes.Agencias.ESTADO_TRAMITADO, new DateTime(2026, 10, 30)), DosEnvios());

            Assert.AreEqual(HttpStatusCode.OK, pagina.Estado);
            StringAssert.Contains(pagina.Html, "Fotos de su pedido");
            string foto = "https://api.nuevavision.es/" + EnlacePublicoFotoBulto.RutaFotoPacking(CLAVE_FOTOS, 7, ID_FOTO);
            StringAssert.Contains(pagina.Html, "src=\"" + foto + "\"");
            Assert.AreEqual(2, Regex.Matches(pagina.Html, "class=\"miniatura\"").Count);
            StringAssert.Contains(pagina.Html, "href=\"#foto-1\"");
            StringAssert.Contains(pagina.Html, "id=\"foto-1\"");
            StringAssert.Contains(pagina.Html, ".ampliada:target{display:flex}");
            StringAssert.Contains(pagina.Html, "loading=\"lazy\"");
            Assert.IsFalse(pagina.Html.Contains("/40-"), "la foto de la caja del primer envío no sale");
        }

        [TestMethod]
        public async Task Pagina_SinFotos_SinGaleria()
        {
            DatosTrazabilidadEnvio datos = DosEnvios();
            datos.FotosPacking.Clear();
            datos.Bultos.ForEach(b => b.TieneFoto = false);

            PaginaSeguimientoReparto pagina = await Pagina(Envio(ENVIO, Constantes.Agencias.ESTADO_TRAMITADO, new DateTime(2026, 10, 30)), datos);

            Assert.IsFalse(pagina.Html.Contains("Fotos de su pedido"));
            Assert.IsFalse(pagina.Html.Contains("class=\"miniatura\""));
            StringAssert.Contains(pagina.Html, TrazabilidadEnvio.EMBALADO, "las cajas sin foto sí dicen cuándo se embaló");
        }

        [TestMethod]
        public void Galeria_SinClaveDeFotos_NoSale()
        {
            VistaSeguimientoReparto vista = VistaSeguimientoReparto.Construir(Envio(ENVIO, Constantes.Agencias.ESTADO_TRAMITADO, new DateTime(2026, 10, 30)),
                null, false, DosEnvios(), urlFoto: f => ServicioSeguimientoPublicoReparto.UrlFoto("https://api.nuevavision.es", null, f));

            Assert.AreEqual(0, vista.Fotos.Count, "sin clave no hay enlace que firmar: mejor sin galería que con fotos rotas");
        }

        // ===================================================================== otros envíos

        [TestMethod]
        public async Task Pagina_SegundoEnvio_OtrosEnviosPlegados_ConEnlaceSoloLosDelRepartoPropio()
        {
            PaginaSeguimientoReparto pagina = await Pagina(Envio(ENVIO, Constantes.Agencias.ESTADO_TRAMITADO, new DateTime(2026, 10, 30)), DosEnvios());

            StringAssert.Contains(pagina.Html, "<details class=\"otros\"><summary>Otros envíos de este pedido (2)</summary>");
            Assert.IsFalse(pagina.Html.Contains("<details class=\"otros\" open"), "plegado");
            StringAssert.Contains(pagina.Html, "href=\"" + EnlaceSeguimientoRepartoPropio.Url(ENVIO_ANTERIOR) + "\"");
            Assert.IsFalse(pagina.Html.Contains(EnlaceSeguimientoRepartoPropio.Token(ENVIO_AGENCIA)), "el de agencia, sin enlace nuestro");
            StringAssert.Contains(pagina.Html, "Entregado el 29 de octubre de 2026");
            StringAssert.Contains(pagina.Html, "En camino");
            StringAssert.Contains(pagina.Html, "Envío del 20 de octubre de 2026");
            Assert.AreEqual(1, Regex.Matches(pagina.Html, TrazabilidadEnvio.EMBALADO).Count, "los hitos del otro envío no se mezclan");
            Assert.IsTrue(pagina.Html.IndexOf("Historia del envío") < pagina.Html.IndexOf("Otros envíos de este pedido"), "arriba la línea de tiempo de este");
        }

        [TestMethod]
        public async Task Pagina_UnSoloEnvio_SinOtrosEnvios()
        {
            var datos = new DatosTrazabilidadEnvio { Envios = { DelPedido(ENVIO, new DateTime(2026, 10, 30)) } };

            PaginaSeguimientoReparto pagina = await Pagina(Envio(ENVIO, Constantes.Agencias.ESTADO_TRAMITADO, new DateTime(2026, 10, 30)), datos);

            Assert.IsFalse(pagina.Html.Contains("Otros envíos de este pedido"));
        }

        [TestMethod]
        public async Task Pagina_SiFallaLaTrazabilidad_SaleLaDeSiempre()
        {
            var lector = A.Fake<ILectorSeguimientoPublicoReparto>();
            EnviosAgencia envio = Envio(ENVIO, Constantes.Agencias.ESTADO_TRAMITADO, new DateTime(2026, 10, 30));
            _ = A.CallTo(() => lector.LeerEnvio(ENVIO)).Returns(Task.FromResult(envio));
            _ = A.CallTo(() => lector.LeerHitos(ENVIO)).Returns(Task.FromResult(new List<HitoEntregaPublico>()));
            _ = A.CallTo(() => lector.LeerTrazabilidad(envio)).Throws(new InvalidOperationException("BD caída"));

            PaginaSeguimientoReparto pagina = await new ServicioSeguimientoPublicoReparto(lector, claveFotos: () => CLAVE_FOTOS).Pagina(EnlaceSeguimientoRepartoPropio.Token(ENVIO));

            Assert.AreEqual(HttpStatusCode.OK, pagina.Estado);
            StringAssert.Contains(pagina.Html, "En reparto");
            StringAssert.Contains(pagina.Html, TrazabilidadEnvio.PREPARADO_PARA_EL_REPARTO);
        }

        // ===================================================================== privacidad

        [TestMethod]
        public async Task Pagina_NuncaNombresDeEmpleadosDniFirmaNiGps()
        {
            var hitos = new List<HitoEntregaPublico> { new HitoEntregaPublico { Tipo = "Entrega", FechaHora = new DateTime(2026, 10, 31, 10, 0, 0), Receptor = "María" } };

            PaginaSeguimientoReparto pagina = await Pagina(Envio(ENVIO, Constantes.Agencias.ESTADO_ENTREGADO, new DateTime(2026, 10, 30)), DosEnvios(), hitos);

            foreach (string prohibido in new[] { "Usuario", "NUEVAVISION", "Andre", "Pedro", "Dni", "DNI", "Firma", "firma", "Latitud", "Longitud", "data:image" })
            {
                Assert.IsFalse(pagina.Html.Contains(prohibido), prohibido);
            }
            // Las clases de la trazabilidad no tienen dónde llevarlo (y las consultas no leen Usuario)
            foreach (Type tipo in new[] { typeof(DatosTrazabilidadEnvio), typeof(EnvioDelPedidoPublico), typeof(PickingDelPedidoPublico), typeof(BultoTrazabilidad),
                typeof(FotoPackingTrazabilidad), typeof(FotoDeLaGaleria), typeof(FotoGaleriaPublica), typeof(OtroEnvioPublico), typeof(EventoSeguimientoPublico) })
            {
                foreach (string propiedad in new[] { "Usuario", "Repartidor", "Empleado", "DniReceptor", "Firma", "Latitud", "Longitud", "Dispositivo" })
                {
                    Assert.IsNull(tipo.GetProperty(propiedad), $"{tipo.Name} no puede llevar {propiedad}");
                }
            }
            foreach (string sql in new[] { LectorSeguimientoPublicoReparto.SQL_BULTOS, LectorSeguimientoPublicoReparto.SQL_FOTOS_PACKING,
                LectorSeguimientoPublicoReparto.SQL_INICIO_PICKING, LectorSeguimientoPublicoReparto.SQL_PRIMERA_CARGA, LectorSeguimientoPublicoReparto.SQL_PICKINGS_DE_LAS_LINEAS })
            {
                Assert.IsFalse(sql.Contains("Usuario"), sql);
            }
        }

        [TestMethod]
        public async Task Pagina_EsMovilYClaroOscuro_ConLaGaleria()
        {
            PaginaSeguimientoReparto pagina = await Pagina(Envio(ENVIO, Constantes.Agencias.ESTADO_TRAMITADO, new DateTime(2026, 10, 30)), DosEnvios());

            StringAssert.Contains(pagina.Html, "name=\"viewport\"");
            StringAssert.Contains(pagina.Html, "prefers-color-scheme:dark");
            StringAssert.Contains(pagina.Html, "grid-template-columns:repeat(auto-fill,minmax(96px,1fr))");
        }

        /// <summary>
        /// La página de muestra para que Carlos la vea (dos envíos, con fotos). No comprueba nada nuevo: la deja en
        /// %TEMP%\Seguimiento771_Muestra.html (o donde diga la variable SEGUIMIENTO771_MUESTRA).
        /// </summary>
        [TestMethod]
        public async Task Muestra_GuardaUnaPaginaDeEjemplo()
        {
            var hitos = new List<HitoEntregaPublico>
            {
                new HitoEntregaPublico { Tipo = "Incidencia", FechaHora = new DateTime(2026, 10, 30, 17, 10, 0), Motivo = IncidenciaRepartoDTO.AUSENTE },
                new HitoEntregaPublico { Tipo = "Entrega", FechaHora = new DateTime(2026, 10, 31, 10, 25, 0), Receptor = "María López" }
            };
            PaginaSeguimientoReparto pagina = await Pagina(Envio(ENVIO, Constantes.Agencias.ESTADO_ENTREGADO, new DateTime(2026, 10, 30)), DosEnvios(), hitos);
            string ruta = Environment.GetEnvironmentVariable("SEGUIMIENTO771_MUESTRA");
            if (string.IsNullOrWhiteSpace(ruta))
            {
                ruta = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Seguimiento771_Muestra.html");
            }
            System.IO.File.WriteAllText(ruta, pagina.Html, new System.Text.UTF8Encoding(false));
            Assert.IsTrue(System.IO.File.Exists(ruta));
        }
    }
}
