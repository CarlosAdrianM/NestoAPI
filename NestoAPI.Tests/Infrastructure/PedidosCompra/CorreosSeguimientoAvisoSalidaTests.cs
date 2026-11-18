using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using NestoAPI.Infraestructure.PedidosVenta;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PedidosCompra
{
    /// <summary>
    /// NestoAPI#857e: los avisos de salida del proveedor (remitente marcado sin agencia, filtro de texto y PDF adjunto). Caso real del
    /// 17/11/26: sales@dduueett.com (proveedor 1021) manda «Adjuntamos albarán de entrega» con el packing list del pedido 220923 por Schenker.
    /// </summary>
    [TestClass]
    public class CorreosSeguimientoAvisoSalidaTests
    {
        /// <summary>El texto del PDF real (packing list de DDUUEETT del 17/11/26).</summary>
        internal const string PDF_DDUUEETT = @"NUEVA VISION SA
DESCRIPCIÓN CANTIDAD
PACKING
LIST
PL
AE DVA26 -26004502 REF 220923 FECHA 17/11/2026 NIF ESA78368255
CÓDIGO COMENTARIOS DESTINATARIO VOL
Compañía de transporte: SCHENKER STANDARD
DVP - 26004726
PALLET: 000000000000318143 - 3000050011 PALET 60X80 (11)
Gross Weight: 37,42 Kg Wide: 60,00 cm Deep: 84,00 cm High: 71,00 cm Volume: 0,36 m3
1690030051 BROCK (51) - 220923 1,00 0,05
1410760051 HALI (51) - 220923 1,00 0,09
1690020051 BAZZ TABLE (51) - 220923 1,00 0,04
1620150051 THOR GAL (51) - 220923 2,00 0,02
CONDICIONES ESPECIALES
Fecha Entrega: Hora Entrega:
INSTRUCCIONES LOGISTICA: SCHENKER STANDARD
00:00 Cita Previa: N Plataforma: N
...
INCOTERMS: DAP VOLUMEN TOTAL: 0.36 m3 CANTIDAD PALLETS: 1 U PESO BRUTO TOTAL: 37.42Kg
Dduueett Europe S.L.";

        internal const string CUERPO_DDUUEETT = @"Apreciado cliente,
Adjuntamos albarán de entrega.

Please find enclosed delivery note.

Dduueett Europe S.L.
C/ Font de la Ventaiola
Navàs (Barcelona)";

        internal const string FILTRO = "albarán de entrega|delivery note";
        private static readonly DateTime RECIBIDO = new DateTime(2026, 11, 17, 11, 30, 0);
        private static readonly DateTime AHORA = new DateTime(2026, 11, 17, 12, 0, 0);
        private static readonly DateTime FECHA_PRUDENTE = new DateTime(2026, 12, 10);

        private static PedidoCompraEnAire Pedido(int numero, string proveedor = "1021") => new PedidoCompraEnAire
        {
            Empresa = "1", Pedido = numero, Proveedor = proveedor, NombreProveedor = "DDUUEETT EUROPE, S.L.", Web = "www.dduueett.com"
        };

        private static RemitenteMarcado Dduueett(string filtro = FILTRO, bool leerPdf = true) => new RemitenteMarcado
        {
            Id = 1, Agencia = null, Remitente = "sales@dduueett.com", Activo = true, FiltroTexto = filtro, LeerPdf = leerPdf
        };

        private static CorreoCompras Aviso(string asunto = "Albarán de entrega DVP26004726", string cuerpo = CUERPO_DDUUEETT, bool conAdjuntos = true) => new CorreoCompras
        {
            Id = "graph-dduueett", MensajeId = "<albaran-26004726@dduueett.com>", Recibido = RECIBIDO, RemitenteDireccion = "sales@dduueett.com",
            RemitenteNombre = "Dduueett", Asunto = asunto, Cuerpo = cuerpo, CuerpoEsHtml = false, TieneAdjuntos = conAdjuntos
        };

        #region Extractor (puro, sobre el texto del PDF)

        [TestMethod]
        public void Extractor_PdfReal_ElPedido220923DeEntreLosDosEnElAire_SchenkerYSinFecha()
        {
            List<PedidoCompraEnAire> enAire = new List<PedidoCompraEnAire> { Pedido(220923), Pedido(220782), Pedido(220900, "9999") };

            AvisoSalidaPdf aviso = ExtractorAvisoSalida.Extraer(PDF_DDUUEETT, RECIBIDO, enAire.Where(p => p.Proveedor == "1021"), enAire,
                AgenciasEntradaRegistradas.Todas);

            Assert.AreEqual(220923, aviso.Pedido?.Pedido);
            Assert.AreEqual("DB Schenker", aviso.Agencia?.Nombre);
            Assert.AreEqual(48, aviso.Agencia.HorasHastaEntrega);
            Assert.IsNull(aviso.Entrega, "«Fecha Entrega:» viene vacía: no hay fecha (la del documento no es de entrega)");
        }

        [TestMethod]
        public void Extractor_NumeroQueNoEsDeNingunPedidoEnElAire_Ninguno()
        {
            var suyos = new List<PedidoCompraEnAire> { Pedido(220782), Pedido(220001) };
            Assert.IsNull(ExtractorAvisoSalida.Pedido(PDF_DDUUEETT, suyos, suyos));
        }

        [TestMethod]
        public void Extractor_NumeroSueltoDeUnoSoloDelProveedor_Vale()
        {
            var suyos = new List<PedidoCompraEnAire> { Pedido(220923), Pedido(220782) };
            Assert.AreEqual(220923, ExtractorAvisoSalida.Pedido("Pedido de cliente 777\nLínea 1: BROCK - 220923 1,00", suyos, suyos)?.Pedido);
        }

        [TestMethod]
        public void Extractor_DosPedidosDelProveedorEtiquetados_Ninguno()
        {
            var suyos = new List<PedidoCompraEnAire> { Pedido(220923), Pedido(220782) };
            Assert.IsNull(ExtractorAvisoSalida.Pedido("Su pedido 220923\nSu pedido 220782", suyos, suyos));
        }

        [TestMethod]
        public void Extractor_EtiquetadoGanaAlSuelto()
        {
            var suyos = new List<PedidoCompraEnAire> { Pedido(220923), Pedido(220782) };
            Assert.AreEqual(220782, ExtractorAvisoSalida.Pedido("Your order: 220782\nLote 220923", suyos, suyos)?.Pedido);
        }

        [TestMethod]
        public void Extractor_SinProveedorReconocido_SoloEtiquetadoDeUnPedidoEnElAire()
        {
            var enAire = new List<PedidoCompraEnAire> { Pedido(220923, "1021"), Pedido(220782, "5555") };
            Assert.AreEqual(220923, ExtractorAvisoSalida.Pedido(PDF_DDUUEETT, null, enAire)?.Pedido);
            Assert.IsNull(ExtractorAvisoSalida.Pedido("Bulto 220923 1,00", null, enAire), "Suelto y sin proveedor: no basta");
        }

        [TestMethod]
        public void Extractor_SinTexto_Nada()
        {
            AvisoSalidaPdf aviso = ExtractorAvisoSalida.Extraer("  ", RECIBIDO, new[] { Pedido(220923) }, new[] { Pedido(220923) }, null);
            Assert.IsNull(aviso.Pedido);
            Assert.IsNull(aviso.Agencia);
        }

        [TestMethod]
        public void Completar_ConPedidoDelPdf_InequivocoConMotivoPdfAdjuntoYAgenciaDelPdf()
        {
            var analisis = new AnalisisCorreoSeguimiento { Guardar = true, AvisoProveedor = true };
            var asignacion = new AsignacionPedidoCompra
            {
                ProveedorPropuesto = "1021",
                Candidatos = new List<CandidatoPedidoCompra>
                {
                    new CandidatoPedidoCompra { Empresa = "1", Pedido = 220923, Proveedor = "1021", Motivo = AsignadorPedidosCompra.MOTIVO_DOMINIO },
                    new CandidatoPedidoCompra { Empresa = "1", Pedido = 220782, Proveedor = "1021", Motivo = AsignadorPedidosCompra.MOTIVO_DOMINIO }
                }
            };
            var pdf = new AvisoSalidaPdf { Pedido = Pedido(220923), Agencia = AgenciasEntradaRegistradas.PorNombre("DB Schenker") };

            AsignacionPedidoCompra nueva = AvisoSalidaProveedor.Completar(analisis, asignacion, pdf);

            CandidatoPedidoCompra unico = nueva.Candidatos.Single();
            Assert.AreEqual(220923, unico.Pedido);
            Assert.AreEqual(AsignadorPedidosCompra.MOTIVO_PDF_ADJUNTO, unico.Motivo);
            Assert.AreEqual("DB Schenker", analisis.Agencia);
            Assert.AreEqual(48, analisis.HorasHastaEntrega);
        }

        [TestMethod]
        public void Completar_SinPedidoEnElPdf_LaAsignacionDeSiempre()
        {
            var asignacion = new AsignacionPedidoCompra { Candidatos = new List<CandidatoPedidoCompra> { new CandidatoPedidoCompra { Pedido = 1 }, new CandidatoPedidoCompra { Pedido = 2 } } };
            Assert.AreSame(asignacion, AvisoSalidaProveedor.Completar(new AnalisisCorreoSeguimiento(), asignacion, new AvisoSalidaPdf()));
        }

        [TestMethod]
        public void Completar_LaAgenciaDelCorreoMandaSobreLaDelPdf()
        {
            var analisis = new AnalisisCorreoSeguimiento { Agencia = "GLS", HorasHastaEntrega = 24 };
            _ = AvisoSalidaProveedor.Completar(analisis, null, new AvisoSalidaPdf { Agencia = AgenciasEntradaRegistradas.PorNombre("DB Schenker") });
            Assert.AreEqual("GLS", analisis.Agencia);
        }

        [TestMethod]
        public void EstadoConjunto_ElMejor()
        {
            Assert.AreEqual(EstadoPdfAdjunto.Leido, AvisoSalidaProveedor.EstadoConjunto(new[] { EstadoPdfAdjunto.SinTexto, EstadoPdfAdjunto.Leido }));
            Assert.AreEqual(EstadoPdfAdjunto.SinTexto, AvisoSalidaProveedor.EstadoConjunto(new[] { EstadoPdfAdjunto.Grande, EstadoPdfAdjunto.SinTexto }));
            Assert.AreEqual(EstadoPdfAdjunto.SinPdf, AvisoSalidaProveedor.EstadoConjunto(new EstadoPdfAdjunto[0]));
        }

        #endregion

        #region Filtro de texto y remitente sin agencia (analizador)

        [DataTestMethod]
        [DataRow("Adjuntamos ALBARAN DE ENTREGA.", true)]
        [DataRow("Please find enclosed delivery note.", true)]
        [DataRow("Adjuntamos la factura 2026/1234.", false)]
        [DataRow("Albaranes de entregas pendientes", false)]
        public void Filtro_CasaSinMayusculasNiTildesYComoPalabras(string cuerpo, bool casa)
        {
            Assert.AreEqual(casa, FiltroRemitente.Casa(FILTRO, "Asunto cualquiera", cuerpo));
        }

        [TestMethod]
        public void Filtro_TambienEnElAsunto_YSinFiltroTodo()
        {
            Assert.IsTrue(FiltroRemitente.Casa(FILTRO, "Su albarán de entrega", "nada"));
            Assert.IsTrue(FiltroRemitente.Casa(null, "Factura", "nada"));
            Assert.IsTrue(FiltroRemitente.Casa(" | ", "Factura", "nada"));
        }

        [TestMethod]
        public void Filtro_Limpiar()
        {
            Assert.AreEqual("albarán de entrega|delivery note", FiltroRemitente.Limpiar(" albarán de entrega | | delivery note "));
            Assert.IsNull(FiltroRemitente.Limpiar(" | - | "));
            Assert.IsNull(FiltroRemitente.Limpiar(null));
        }

        [TestMethod]
        public void Analizador_RemitenteSinAgenciaConFiltroQueCasa_SeGuardaComoAvisoDelProveedor()
        {
            AnalisisCorreoSeguimiento analisis = AnalizadorCorreoSeguimiento.Analizar(Aviso(), AgenciasEntradaRegistradas.Todas, new[] { Dduueett() });

            Assert.IsTrue(analisis.Guardar);
            Assert.IsTrue(analisis.AvisoProveedor);
            Assert.IsNull(analisis.Agencia, "El cuerpo no nombra la agencia: la dirá el PDF");
            Assert.IsFalse(analisis.Seguimientos.Any());
        }

        [TestMethod]
        public void Analizador_RemitenteConFiltroQueNoCasa_NoSeGuarda()
        {
            CorreoCompras factura = Aviso("Factura FV26-1234", "Adjuntamos la factura del mes. Un saludo.");
            AnalisisCorreoSeguimiento analisis = AnalizadorCorreoSeguimiento.Analizar(factura, AgenciasEntradaRegistradas.Todas, new[] { Dduueett() });

            Assert.IsFalse(analisis.Guardar);
            Assert.IsNull(analisis.RemitenteMarcado);
        }

        [TestMethod]
        public void Analizador_RemitenteDeAgenciaSinFiltro_ComoAntes()
        {
            var gls = new RemitenteMarcado { Agencia = "GLS", Remitente = "dduueett.com", Activo = true };
            AnalisisCorreoSeguimiento analisis = AnalizadorCorreoSeguimiento.Analizar(Aviso("Factura", "La factura."), AgenciasEntradaRegistradas.Todas, new[] { gls });

            Assert.IsTrue(analisis.Guardar);
            Assert.IsFalse(analisis.AvisoProveedor);
            Assert.AreEqual("GLS", analisis.Agencia);
        }

        [TestMethod]
        public void Analizador_AvisoSinAgenciaQueLaNombraEnElCuerpo_LaToma()
        {
            CorreoCompras aviso = Aviso(cuerpo: CUERPO_DDUUEETT + "\nTransportista: DB Schenker");
            AnalisisCorreoSeguimiento analisis = AnalizadorCorreoSeguimiento.Analizar(aviso, AgenciasEntradaRegistradas.Todas, new[] { Dduueett() });
            Assert.AreEqual("DB Schenker", analisis.Agencia);
        }

        #endregion

        #region El job, de punta a punta (Graph y lector de PDF falsos)

        private RepositorioCorreosSeguimientoEnMemoria repositorio;
        private ILectorBuzonCompras lector;
        private ILectorTextoPdf lectorPdf;
        private List<Exception> errores;
        private bool interruptor;
        private ResultadoTextoPdf textoPdf;
        private List<(CorreoCompras Correo, CorreoSeguimientoFila Fila, ResultadoCorreoGuardado Resultado)> reenviados;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = new RepositorioCorreosSeguimientoEnMemoria();
            repositorio.AnadirPedido(220923, "1021", "DDUUEETT EUROPE, S.L.", new DateTime(2026, 11, 17, 9, 0, 0), FECHA_PRUDENTE, FECHA_PRUDENTE);
            repositorio.Pedidos.Last().Web = "www.dduueett.com";
            repositorio.AnadirPedido(220782, "1021", "DDUUEETT EUROPE, S.L.", new DateTime(2026, 11, 3, 9, 0, 0), new DateTime(2026, 11, 26));
            repositorio.Pedidos.Last().Web = "www.dduueett.com";
            repositorio.Remitentes.Add(Dduueett());
            errores = new List<Exception>();
            interruptor = true;
            reenviados = new List<(CorreoCompras, CorreoSeguimientoFila, ResultadoCorreoGuardado)>();
            textoPdf = new ResultadoTextoPdf { Estado = EstadoPdfAdjunto.Leido, Texto = PDF_DDUUEETT };

            lector = A.Fake<ILectorBuzonCompras>();
            A.CallTo(() => lector.Buzon).Returns("buzon@ejemplo.test");
            A.CallTo(() => lector.LeerPdfs(A<string>._, A<int>._, A<long>._)).Returns(new List<PdfAdjunto>
            {
                new PdfAdjunto { Nombre = "PL26004726.pdf", Tamano = 40000, Contenido = new byte[] { 1, 2, 3 } }
            });
            lectorPdf = A.Fake<ILectorTextoPdf>();
            A.CallTo(() => lectorPdf.Leer(A<byte[]>._, A<int>._)).ReturnsLazily(() => textoPdf);
        }

        private async Task Pasada(params CorreoCompras[] correos)
        {
            A.CallTo(() => lector.LeerNuevos(A<string>._, A<DateTime>._)).Returns(new LecturaBuzonCompras { Correos = correos.ToList() });
            var aplicacion = new ServicioAplicacionCorreosCompras(repositorio, () => interruptor, A.Fake<IAvisadorCorreosPorAsignar>(),
                (empresa, productos, descripcion, causas) => { }, () => AHORA, errores.Add);
            IReenviadorCorreosSeguimiento reenviador = A.Fake<IReenviadorCorreosSeguimiento>();
            A.CallTo(() => reenviador.Reenviar(A<IEnumerable<(CorreoCompras, CorreoSeguimientoFila, ResultadoCorreoGuardado)>>._))
                .ReturnsLazily((IEnumerable<(CorreoCompras, CorreoSeguimientoFila, ResultadoCorreoGuardado)> lista) =>
                {
                    reenviados.AddRange(lista);
                    return lista.Count();
                });
            var servicio = new ServicioCorreosSeguimientoCompras(lector, repositorio, errores.Add, horaCorte: () => new TimeSpan(11, 0, 0),
                esFestivo: _ => false, aplicacion: aplicacion, reenviador: reenviador, lectorPdf: lectorPdf);
            _ = await servicio.Pasada(AHORA);
        }

        [TestMethod]
        public async Task Job_AvisoConPdf_SeGuardaYSeAplicaEl19AlPedido220923()
        {
            await Pasada(Aviso());

            CorreoSeguimientoFila fila = repositorio.Correos.Single();
            Assert.AreEqual(220923, fila.PedidoEnAdjunto);
            Assert.AreEqual("Leido", fila.AdjuntoLeido);
            Assert.AreEqual(true, fila.AvisoProveedor);
            Assert.AreEqual("DB Schenker", fila.Agencia);
            Assert.IsNull(fila.Codigo);
            Assert.AreEqual(AsignadorPedidosCompra.MOTIVO_PDF_ADJUNTO, AsignadorPedidosCompra.Deserializar(fila.PedidosCandidatos).Single().Motivo);
            Assert.AreEqual(220923, fila.PedidoAplicado);
            DateTime dia19 = new DateTime(2026, 11, 19);
            Assert.AreEqual(dia19, fila.FechaRecepcionAplicada, "Recibido el 17 + 48 h de Schenker");
            Assert.IsTrue(repositorio.Lineas[220923].All(l => l.FechaRecepcion == dia19));
            Assert.IsTrue(repositorio.Lineas[220782].All(l => l.FechaRecepcion == new DateTime(2026, 11, 26)), "El otro pedido no se toca");
            Assert.AreEqual(0, errores.Count, string.Join(" | ", errores.Select(e => e.Message)));

            // El reenvío temporal a Compras explica de dónde sale
            var (_, filaReenviada, resultado) = reenviados.Single();
            string cabecera = WebUtility.HtmlDecode(TextosReenvioCorreoSeguimiento.Cabecera(filaReenviada, resultado, "sales@dduueett.com", RECIBIDO));
            StringAssert.Contains(cabecera, "Aviso de salida del proveedor: pedido 220923 leído del PDF adjunto.");
            StringAssert.Contains(cabecera, "10/12/2026 → 19/11/2026");
        }

        [TestMethod]
        public async Task Job_InterruptorApagado_SeGuardaPeroNoSeAplica()
        {
            interruptor = false;

            await Pasada(Aviso());

            CorreoSeguimientoFila fila = repositorio.Correos.Single();
            Assert.AreEqual(220923, fila.PedidoEnAdjunto);
            Assert.IsNull(fila.PedidoAplicado);
            Assert.IsTrue(repositorio.Lineas[220923].All(l => l.FechaRecepcion == FECHA_PRUDENTE));
            Assert.AreEqual(DesenlaceCorreoSeguimiento.AplicarApagado, reenviados.Single().Resultado.Desenlace);
        }

        [TestMethod]
        public async Task Job_PdfIlegible_SeGuardaSinPedidoYQuedaPorAsignar()
        {
            textoPdf = new ResultadoTextoPdf { Estado = EstadoPdfAdjunto.SinTexto };

            await Pasada(Aviso());

            CorreoSeguimientoFila fila = repositorio.Correos.Single();
            Assert.AreEqual("SinTexto", fila.AdjuntoLeido);
            Assert.IsNull(fila.PedidoEnAdjunto);
            Assert.AreEqual(2, AsignadorPedidosCompra.Deserializar(fila.PedidosCandidatos).Count, "Los dos del proveedor, por dominio");
            Assert.IsNull(fila.PedidoAplicado);
            Assert.AreEqual(DesenlaceCorreoSeguimiento.PorAsignar, reenviados.Single().Resultado.Desenlace);
            StringAssert.Contains(TextosReenvioCorreoSeguimiento.OrigenAviso(fila), "no tiene texto");
        }

        [TestMethod]
        public async Task Job_GraphNoDaLosAdjuntos_ComoSiNoTuvieraPdf()
        {
            A.CallTo(() => lector.LeerPdfs(A<string>._, A<int>._, A<long>._)).Returns(Task.FromResult<List<PdfAdjunto>>(null));

            await Pasada(Aviso());

            Assert.AreEqual("NoDescargado", repositorio.Correos.Single().AdjuntoLeido);
            A.CallTo(() => lectorPdf.Leer(A<byte[]>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Job_FiltroQueNoCasa_NiSeGuardaNiSeLeeElPdf()
        {
            await Pasada(Aviso("Factura FV26-1234", "Adjuntamos la factura del mes."));

            Assert.AreEqual(0, repositorio.Correos.Count);
            A.CallTo(() => lector.LeerPdfs(A<string>._, A<int>._, A<long>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Job_RemitenteSinLeerPdf_NoSeDescarga()
        {
            repositorio.Remitentes.Clear();
            repositorio.Remitentes.Add(Dduueett(leerPdf: false));

            await Pasada(Aviso());

            CorreoSeguimientoFila fila = repositorio.Correos.Single();
            Assert.IsNull(fila.AdjuntoLeido);
            Assert.IsNull(fila.PedidoAplicado, "Sin PDF, los dos pedidos del proveedor: por asignar");
            A.CallTo(() => lector.LeerPdfs(A<string>._, A<int>._, A<long>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Historico_NoLeeElPdf()
        {
            var servicio = new ServicioCorreosSeguimientoCompras(lector, repositorio, errores.Add, lectorPdf: lectorPdf);
            var resultado = new ResultadoPasadaCorreosSeguimiento();

            _ = await servicio.GuardarCorreos(new List<CorreoCompras> { Aviso() }, resultado, _ => Task.FromResult(repositorio.Pedidos.ToList()));

            Assert.AreEqual(1, resultado.Guardados);
            Assert.IsNull(repositorio.Correos.Single().AdjuntoLeido);
            A.CallTo(() => lector.LeerPdfs(A<string>._, A<int>._, A<long>._)).MustNotHaveHappened();
        }

        #endregion

        #region Certeza (#853): un aviso sin seguimiento no es «Prácticamente segura»

        [TestMethod]
        public void Certeza_AvisoDelProveedorSinSeguimiento_NoEsSeguimientoDeAgencia()
        {
            DateTime dia = new DateTime(2026, 11, 19);
            var aviso = new CorreoAplicadoPedidoCompra { Empresa = "1", Pedido = 220923, FechaRecepcionAplicada = dia, Agencia = "DB Schenker", SinSeguimientoAgencia = true };
            Assert.IsNull(ReglaSeguimientoAgencia.Vigente(new[] { aviso }, "1", 220923, dia));

            var ups = new CorreoAplicadoPedidoCompra { Empresa = "1", Pedido = 220923, FechaRecepcionAplicada = dia, Agencia = "UPS" };
            Assert.AreEqual("UPS", ReglaSeguimientoAgencia.Vigente(new[] { aviso, ups }, "1", 220923, dia)?.Agencia);
        }

        #endregion

        #region Reenvío: de dónde sale

        [TestMethod]
        public void OrigenAviso_CorreoDeAgenciaDeSiempre_Nada()
        {
            Assert.IsNull(TextosReenvioCorreoSeguimiento.OrigenAviso(new CorreoSeguimientoFila { Agencia = "GLS", Codigo = "123456" }));
            Assert.IsNull(TextosReenvioCorreoSeguimiento.OrigenAviso(new CorreoSeguimientoFila { AvisoProveedor = false }));
        }

        [TestMethod]
        public void OrigenAviso_ConPedidoDelPdf()
        {
            Assert.AreEqual("Aviso de salida del proveedor: pedido 220923 leído del PDF adjunto.",
                TextosReenvioCorreoSeguimiento.OrigenAviso(new CorreoSeguimientoFila { AvisoProveedor = true, PedidoEnAdjunto = 220923, AdjuntoLeido = "Leido" }));
        }

        #endregion

        #region Lectura real de un PDF (PdfPig)

        /// <summary>Un PDF mínimo de una página con esas líneas en Helvetica (WinAnsi: admite tildes y eñes), con su tabla xref bien hecha.</summary>
        internal static byte[] PdfMinimo(params string[] lineas)
        {
            Encoding latin1 = Encoding.GetEncoding(1252);
            var contenido = new StringBuilder("BT /F1 11 Tf 14 TL 50 780 Td\n");
            foreach (string linea in lineas)
            {
                _ = contenido.Append('(').Append(linea.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)")).Append(") Tj T*\n");
            }
            _ = contenido.Append("ET");
            byte[] flujo = latin1.GetBytes(contenido.ToString());
            var objetos = new List<byte[]>
            {
                latin1.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"),
                latin1.GetBytes("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
                latin1.GetBytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>"),
                latin1.GetBytes($"<< /Length {flujo.Length} >>\nstream\n").Concat(flujo).Concat(latin1.GetBytes("\nendstream")).ToArray(),
                latin1.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>")
            };
            using (var pdf = new MemoryStream())
            {
                void Escribir(string s) { byte[] b = latin1.GetBytes(s); pdf.Write(b, 0, b.Length); }
                Escribir("%PDF-1.4\n");
                var posiciones = new List<long>();
                for (int i = 0; i < objetos.Count; i++)
                {
                    posiciones.Add(pdf.Position);
                    Escribir($"{i + 1} 0 obj\n");
                    pdf.Write(objetos[i], 0, objetos[i].Length);
                    Escribir("\nendobj\n");
                }
                long xref = pdf.Position;
                Escribir($"xref\n0 {objetos.Count + 1}\n0000000000 65535 f \n");
                foreach (long p in posiciones)
                {
                    Escribir($"{p:D10} 00000 n \n");
                }
                Escribir($"trailer\n<< /Size {objetos.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
                return pdf.ToArray();
            }
        }

        [TestMethod]
        public void PdfPig_PdfDeTextoReal_SacaLasLineasYElExtractorElPedido()
        {
            byte[] pdf = PdfMinimo("PACKING LIST", "AE DVA26 -26004502 REF 220923 FECHA 17/11/2026", "Compañía de transporte: SCHENKER STANDARD",
                "1690030051 BROCK (51) - 220923 1,00 0,05");

            ResultadoTextoPdf leido = new LectorTextoPdfPig().Leer(pdf, LimitesPdfAdjuntos.MAXIMO_PAGINAS);

            Assert.AreEqual(EstadoPdfAdjunto.Leido, leido.Estado, leido.Texto);
            StringAssert.Contains(leido.Texto, "REF 220923");
            StringAssert.Contains(leido.Texto, "Compañía de transporte: SCHENKER STANDARD");
            var suyos = new List<PedidoCompraEnAire> { Pedido(220923), Pedido(220782) };
            AvisoSalidaPdf aviso = ExtractorAvisoSalida.Extraer(leido.Texto, RECIBIDO, suyos, suyos, AgenciasEntradaRegistradas.Todas);
            Assert.AreEqual(220923, aviso.Pedido?.Pedido);
            Assert.AreEqual("DB Schenker", aviso.Agencia?.Nombre);
        }

        [TestMethod]
        public void PdfPig_PdfSinTexto_SinTexto()
        {
            Assert.AreEqual(EstadoPdfAdjunto.SinTexto, new LectorTextoPdfPig().Leer(PdfMinimo(), 10).Estado);
        }

        [TestMethod]
        public void PdfPig_BytesQueNoSonUnPdf_IlegibleSinLanzar()
        {
            Assert.AreEqual(EstadoPdfAdjunto.Ilegible, new LectorTextoPdfPig().Leer(Encoding.ASCII.GetBytes("esto no es un PDF"), 10).Estado);
            Assert.AreEqual(EstadoPdfAdjunto.Ilegible, new LectorTextoPdfPig().Leer(null, 10).Estado);
        }

        [TestMethod]
        public void LeerConTope_PasaDelTope_Null()
        {
            Assert.IsNull(LimitesPdfAdjuntos.LeerConTope(new MemoryStream(new byte[100]), 99));
            Assert.AreEqual(100, LimitesPdfAdjuntos.LeerConTope(new MemoryStream(new byte[100]), 100).Length);
        }

        [TestMethod]
        public void EsPdf_PorTipoONombre()
        {
            Assert.IsTrue(LimitesPdfAdjuntos.EsPdf("PL.PDF", "application/octet-stream"));
            Assert.IsTrue(LimitesPdfAdjuntos.EsPdf("albaran", "application/pdf"));
            Assert.IsFalse(LimitesPdfAdjuntos.EsPdf("foto.jpg", "image/jpeg"));
        }

        #endregion

        #region Endpoint de remitentes

        private static ComprasAgenciasEntradaController Controller(IRepositorioCorreosSeguimiento repo) => new ComprasAgenciasEntradaController(repo)
        {
            User = new GenericPrincipal(new GenericIdentity(@"NUEVAVISION\Prueba", "Bearer"), new[] { "Compras" }),
            Request = new HttpRequestMessage()
        };

        [TestMethod]
        public async Task Post_RemitenteDeProveedorConFiltroYPdf_SeGuarda()
        {
            IRepositorioCorreosSeguimiento repo = A.Fake<IRepositorioCorreosSeguimiento>();
            A.CallTo(() => repo.TablasExisten()).Returns(true);
            A.CallTo(() => repo.AvisosProveedorDisponibles()).Returns(true);
            A.CallTo(() => repo.GuardarRemitente(null, "sales@dduueett.com", A<string>._, FILTRO, true, false)).Returns(7);

            var resultado = await Controller(repo).PostRemitente(new PeticionRemitenteAgenciaDTO
            {
                Agencia = " ", Remitente = "Sales@Dduueett.com", FiltroTexto = " albarán de entrega | delivery note ", LeerPdf = true
            }) as OkNegotiatedContentResult<RemitenteMarcado>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(7, resultado.Content.Id);
            Assert.IsNull(resultado.Content.Agencia);
            Assert.AreEqual(FILTRO, resultado.Content.FiltroTexto);
            Assert.IsTrue(resultado.Content.LeerPdf);
        }

        [TestMethod]
        public async Task Post_SinScript_SinAgencia400_ConPdf503()
        {
            IRepositorioCorreosSeguimiento repo = A.Fake<IRepositorioCorreosSeguimiento>();
            A.CallTo(() => repo.TablasExisten()).Returns(true);
            A.CallTo(() => repo.AvisosProveedorDisponibles()).Returns(false);

            Assert.IsInstanceOfType(await Controller(repo).PostRemitente(new PeticionRemitenteAgenciaDTO { Remitente = "sales@dduueett.com" }),
                typeof(BadRequestErrorMessageResult));
            var conPdf = await Controller(repo).PostRemitente(new PeticionRemitenteAgenciaDTO { Agencia = "GLS", Remitente = "gls-spain.es", LeerPdf = true })
                as NegotiatedContentResult<string>;
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, conPdf.StatusCode);
            A.CallTo(() => repo.GuardarRemitente(A<string>._, A<string>._, A<string>._, A<string>._, A<bool>._, A<bool>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Post_FiltroSinNingunaPalabra_400()
        {
            IRepositorioCorreosSeguimiento repo = A.Fake<IRepositorioCorreosSeguimiento>();
            A.CallTo(() => repo.TablasExisten()).Returns(true);
            A.CallTo(() => repo.AvisosProveedorDisponibles()).Returns(true);
            Assert.IsInstanceOfType(await Controller(repo).PostRemitente(new PeticionRemitenteAgenciaDTO { Remitente = "sales@dduueett.com", FiltroTexto = "| - |" }),
                typeof(BadRequestErrorMessageResult));
        }

        #endregion
    }
}
