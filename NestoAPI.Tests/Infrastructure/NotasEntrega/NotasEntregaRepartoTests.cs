using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.ExtractosRuta;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Picking;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.Traspasos;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Tests.Infrastructure.NotasEntrega
{
    /// <summary>
    /// NestoAPI#758 (corte 2 de #755), con los ajustes de Carlos del 30/10/26: la nota entra en el reparto propio como parada
    /// (envío de la agencia 14), el PDF con su título y su Code128 NE/NR, las etiquetas NR{pedido}-{N} solo de las recogidas, y
    /// la lista de rutas propias del parámetro RutasRepartoPropio (por defecto AT y 16).
    /// </summary>
    [TestClass]
    public class NotasEntregaRepartoTests
    {
        private const string EMPRESA = "1";
        private const int NOTA = 930500;
        private const string CLIENTE = "15191";

        private RepositorioCabecerasFalso cabeceras;
        private RepositorioParadasFalso paradas;
        private List<Exception> registrados;
        private bool repartoPropioActivo;
        private ServicioParadasNotaEntrega servicioParadas;

        [TestInitialize]
        public void Preparar()
        {
            RutasPropiasConfiguradas.Reiniciar();
            cabeceras = new RepositorioCabecerasFalso();
            cabeceras.Pedidos[NOTA] = new PedidoNotaEntrega { Cliente = CLIENTE, NotaEntrega = true };
            paradas = new RepositorioParadasFalso();
            paradas.Destinos[NOTA] = new DestinoNotaEntrega
            {
                Cliente = CLIENTE, Contacto = "0", Nombre = "PELUQUERÍA LA BONITA", Direccion = "C/ MAYOR, 1", CodPostal = "28001",
                Poblacion = "MADRID", Provincia = "MADRID", Telefonos = "915551234/666123456", Texto = "Recoger el sillón - y la lámpara"
            };
            registrados = new List<Exception>();
            repartoPropioActivo = true;
            servicioParadas = new ServicioParadasNotaEntrega(paradas, cabeceras, () => repartoPropioActivo, registrados.Add, () => new DateTime(2026, 10, 30));
        }

        [TestCleanup]
        public void Limpiar() => RutasPropiasConfiguradas.Reiniciar();

        // ================================================================ rutas propias del parámetro

        [TestMethod]
        public void RutasPropias_SinParametro_SonATy16ComoSiempre()
        {
            CollectionAssert.AreEqual(new[] { "AT", "16" }, RutasPropiasConfiguradas.Actuales.ToArray());
            Assert.IsTrue(new RutaPropia().ContieneRuta("16"));
            Assert.IsFalse(new RutaPropia().ContieneRuta("01"));
            Assert.IsFalse(RutasRepartoPropio.EsRutaPropia("01"));
        }

        [TestMethod]
        public void RutasPropias_ConElParametro_TodosLosSitiosUsanLaMismaLista()
        {
            RutasPropiasConfiguradas.Configurar(() => "AT, 16,01;02 03,04,05");

            CollectionAssert.AreEqual(new[] { "AT", "16", "01", "02", "03", "04", "05" }, RutasPropiasConfiguradas.Actuales.ToArray());
            foreach (string ruta in new[] { "01", "05 ", "at" })
            {
                Assert.IsTrue(new RutaPropia().ContieneRuta(ruta), "Facturar rutas " + ruta);
                Assert.IsTrue(RutasRepartoPropio.EsRutaPropia(ruta), "reparto propio " + ruta);
                Assert.IsTrue(ReglasRutaPropia.EsRutaPropia(ruta), "picking " + ruta);
                Assert.IsInstanceOfType(TipoRutaFactory.ObtenerPorNumeroRuta(ruta), typeof(RutaPropia), "copias " + ruta);
            }
            Assert.IsTrue(NestoAPI.Infraestructure.ValidadoresPedido.ValidadorRegaloSinStock.RUTAS_QUE_SALEN_DE_ALGETE.Contains("03"));
            CollectionAssert.AreEqual(RutasPropiasConfiguradas.Actuales.ToArray(), ReglasRutaPropia.RUTAS_PROPIAS.ToArray());
            Assert.IsFalse(RutasRepartoPropio.EsRutaPropia("FW"));
        }

        [TestMethod]
        public void RutasPropias_ParametroVacioOIlegible_SiguenATy16YSeAnotaElFallo()
        {
            RutasPropiasConfiguradas.Configurar(() => "  ");
            CollectionAssert.AreEqual(new[] { "AT", "16" }, RutasPropiasConfiguradas.Actuales.ToArray());

            var fallos = new List<Exception>();
            RutasPropiasConfiguradas.Configurar(() => throw new InvalidOperationException("sin conexión"), fallos.Add);
            CollectionAssert.AreEqual(new[] { "AT", "16" }, RutasPropiasConfiguradas.Actuales.ToArray());
            Assert.AreEqual(1, fallos.Count);
            StringAssert.Contains(fallos[0].Message, RutasPropiasConfiguradas.CLAVE);
        }

        [TestMethod]
        public void RutasPropias_SeLeenUnaVezHastaQueCaducaLaCache()
        {
            int lecturas = 0;
            DateTime ahora = new DateTime(2026, 10, 30, 10, 0, 0);
            string valor = "AT,16";
            RutasPropiasConfiguradas.Configurar(() => { lecturas++; return valor; }, null, () => ahora);

            _ = RutasPropiasConfiguradas.Actuales;
            valor = "AT,16,01";
            _ = RutasPropiasConfiguradas.Actuales;
            Assert.AreEqual(1, lecturas);
            Assert.IsFalse(RutasPropiasConfiguradas.Contiene("01"));

            ahora = ahora + RutasPropiasConfiguradas.DURACION_CACHE;
            Assert.IsTrue(RutasPropiasConfiguradas.Contiene("01"));
            Assert.AreEqual(2, lecturas);
        }

        // ================================================================ códigos NE / NR

        [TestMethod]
        public void CodigoNotaEntrega_FormatoYLectura()
        {
            Assert.AreEqual("NE930500", CodigoNotaEntrega.Documento(false, NOTA));
            Assert.AreEqual("NR930500", CodigoNotaEntrega.Documento(true, NOTA));
            Assert.AreEqual("NR930500-3", CodigoNotaEntrega.BultoRecogida(NOTA, 3));
            Assert.AreEqual("NR930500", TiposNotaEntrega.CodigoDocumento(TiposNotaEntrega.RECOGIDA, NOTA));

            Assert.IsTrue(CodigoNotaEntrega.TryLeer(" nr930500-2 ", out string prefijo, out int pedido, out int? bulto));
            Assert.AreEqual("NR", prefijo);
            Assert.AreEqual(NOTA, pedido);
            Assert.AreEqual(2, bulto);
            Assert.IsTrue(CodigoNotaEntrega.TryLeer("NE930500", out prefijo, out pedido, out bulto));
            Assert.AreEqual("NE", prefijo);
            Assert.IsNull(bulto);
            Assert.IsFalse(CodigoNotaEntrega.TryLeer("NE930500-1", out _, out _, out _), "las entregas no tienen etiqueta propia");
            Assert.IsFalse(CodigoNotaEntrega.TryLeer("RP930500-1", out _, out _, out _));
            Assert.IsFalse(CodigoNotaEntrega.TryLeer("NR0", out _, out _, out _));
            Assert.IsFalse(CodigoNotaEntrega.TryLeer(null, out _, out _, out _));
        }

        // ================================================================ Code128

        [TestMethod]
        public void Code128_TablaDeSimbolos_CadaUnoSuma11YLaParada13()
        {
            Assert.AreEqual(107, Code128.PATRONES.Length);
            for (int i = 0; i < 106; i++)
            {
                Assert.AreEqual(11, Code128.PATRONES[i].Sum(c => c - '0'), "símbolo " + i);
            }
            Assert.AreEqual(13, Code128.PATRONES[106].Sum(c => c - '0'));
        }

        [TestMethod]
        public void Code128_InicioBDatosControlYParada()
        {
            string modulos = new string(Code128.Modulos("NR930500-2").Select(m => m ? '1' : '0').ToArray());

            Assert.AreEqual(11 * (10 + 3) + 2, modulos.Length);
            Assert.IsTrue(modulos.StartsWith("11010010000"), "inicio B");
            Assert.IsTrue(modulos.EndsWith("1100011101011"), "parada");
            // Control: (104 + Σ valor·posición) mod 103, con valor = ASCII − 32.
            string texto = "NR930500-2";
            int suma = 104 + texto.Select((c, i) => (c - 32) * (i + 1)).Sum();
            Assert.AreEqual(suma % 103, Code128.DigitoControl(texto));
            Assert.AreEqual(modulos.Length, Code128.Tramos(texto).Sum(t => t.Modulos));
            Assert.ThrowsException<ArgumentException>(() => Code128.Modulos("ñ"));
        }

        // ================================================================ la parada

        [TestMethod]
        public async Task Parada_RecogidaDeRutaPropia_CreaElEnvioTramitadoConSusBultosYSinReembolso()
        {
            cabeceras.Filas[NOTA] = Fila("Recogida", 3);

            ResultadoParadaNotaEntrega r = await servicioParadas.AsegurarParada(Pedido("AT"), "alfredo");

            Assert.AreEqual(AccionParadaNotaEntrega.Creada, r.Accion);
            Assert.IsTrue(r.EsParada);
            Assert.AreEqual("Recogida", r.Tipo);
            Assert.AreEqual("NR930500", r.CodigoDocumento);
            Assert.AreEqual((short)3, r.Bultos);
            EnviosAgencia envio = paradas.Creados.Single();
            Assert.AreEqual(r.EnvioId, envio.Numero);
            Assert.AreEqual(Constantes.Agencias.AGENCIA_REPARTO_PROPIO, envio.Agencia);
            Assert.AreEqual(Constantes.Agencias.ESTADO_TRAMITADO, envio.Estado);
            Assert.AreEqual((short)3, envio.Bultos);
            Assert.AreEqual(0m, envio.Reembolso);
            Assert.AreEqual(NOTA, envio.Pedido);
            Assert.AreEqual("PELUQUERÍA LA BONITA", envio.Nombre);
            Assert.AreEqual("915551234", envio.Telefono);
            Assert.AreEqual("666123456", envio.Movil);
            Assert.AreEqual("Recoger el sillón - y la lámpara", envio.Observaciones);
            Assert.AreEqual("alfredo", envio.Usuario);
            Assert.AreEqual(new DateTime(2026, 10, 30), envio.Fecha);
            Assert.AreEqual(ServicioParadasNotaEntrega.HISTORIA_RECOGIDA, paradas.Historias.Single());
        }

        [TestMethod]
        public async Task Parada_RecogidaConCeroBultos_UnoComoMinimo()
        {
            cabeceras.Filas[NOTA] = Fila("Recogida", 0);

            ResultadoParadaNotaEntrega r = await servicioParadas.AsegurarParada(Pedido("16"), "alfredo");

            Assert.AreEqual((short)1, paradas.Creados.Single().Bultos);
            Assert.AreEqual((short)1, r.Bultos);
        }

        [TestMethod]
        public async Task Parada_EntregaConElEnvioDelPacking_LoReutilizaYNoCreaNada()
        {
            paradas.Envios.Add(new EnvioPedidoNota { Numero = 251400, Agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO, Estado = Constantes.Agencias.ESTADO_TRAMITADO, Bultos = 2 });

            ResultadoParadaNotaEntrega r = await servicioParadas.AsegurarParada(Pedido("AT"), "alfredo");

            Assert.AreEqual(AccionParadaNotaEntrega.Reutilizada, r.Accion);
            Assert.AreEqual(251400, r.EnvioId);
            Assert.AreEqual((short)2, r.Bultos, "los bultos de una entrega salen del packing");
            Assert.AreEqual("NE930500", r.CodigoDocumento);
            Assert.AreEqual(0, paradas.Creados.Count);
        }

        [TestMethod]
        public async Task Parada_EntregaSinEnvio_CreaLaDeReservaConUnBultoYSuHistoria()
        {
            ResultadoParadaNotaEntrega r = await servicioParadas.AsegurarParada(Pedido("16"), "alfredo");

            Assert.AreEqual(AccionParadaNotaEntrega.Creada, r.Accion);
            Assert.AreEqual("Entrega", r.Tipo);
            EnviosAgencia envio = paradas.Creados.Single();
            Assert.AreEqual((short)1, envio.Bultos);
            Assert.AreEqual(0m, envio.Reembolso);
            Assert.AreEqual(ServicioParadasNotaEntrega.HISTORIA_ENTREGA_SIN_PACKING, paradas.Historias.Single());
        }

        [TestMethod]
        public async Task Parada_RelanzarFacturarRutas_NoCreaDosParadas()
        {
            cabeceras.Filas[NOTA] = Fila("Recogida", 2);

            ResultadoParadaNotaEntrega primera = await servicioParadas.AsegurarParada(Pedido("AT"), "alfredo");
            ResultadoParadaNotaEntrega segunda = await servicioParadas.AsegurarParada(Pedido("AT"), "alfredo");

            Assert.AreEqual(AccionParadaNotaEntrega.Creada, primera.Accion);
            Assert.AreEqual(AccionParadaNotaEntrega.Reutilizada, segunda.Accion);
            Assert.AreEqual(primera.EnvioId, segunda.EnvioId);
            Assert.AreEqual(1, paradas.Creados.Count);
        }

        [TestMethod]
        public async Task Parada_EnvioDeOtraAgencia_NoSeTocaSeAnotaEnElmahYNoEsParada()
        {
            paradas.Envios.Add(new EnvioPedidoNota { Numero = 251399, Agencia = Constantes.Agencias.AGENCIA_CTT, Estado = Constantes.Agencias.ESTADO_TRAMITADO, Bultos = 1 });

            ResultadoParadaNotaEntrega r = await servicioParadas.AsegurarParada(Pedido("AT"), "alfredo");

            Assert.AreEqual(AccionParadaNotaEntrega.OtraAgencia, r.Accion);
            Assert.IsFalse(r.EsParada);
            Assert.AreEqual(0, paradas.Creados.Count);
            Assert.AreEqual(1, registrados.Count);
            StringAssert.Contains(registrados[0].Message, "251399");
        }

        [TestMethod]
        public async Task Parada_EnvioDelRepartoPropioSinTramitar_AvisaYNoDuplica()
        {
            paradas.Envios.Add(new EnvioPedidoNota { Numero = 251401, Agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO, Estado = Constantes.Agencias.ESTADO_EN_CURSO });

            ResultadoParadaNotaEntrega r = await servicioParadas.AsegurarParada(Pedido("AT"), "alfredo");

            Assert.AreEqual(AccionParadaNotaEntrega.SinTramitar, r.Accion);
            StringAssert.Contains(r.Aviso, "tramítelo");
            Assert.AreEqual(0, paradas.Creados.Count);
        }

        [TestMethod]
        public async Task Parada_RutaDeAgenciaOInterruptorApagado_NadaComoHastaAhora()
        {
            cabeceras.Filas[NOTA] = Fila("Recogida", 2);

            ResultadoParadaNotaEntrega agencia = await servicioParadas.AsegurarParada(Pedido("FW"), "alfredo");
            repartoPropioActivo = false;
            ResultadoParadaNotaEntrega apagado = await servicioParadas.AsegurarParada(Pedido("AT"), "alfredo");

            Assert.AreEqual(AccionParadaNotaEntrega.NoAplica, agencia.Accion);
            Assert.AreEqual("Recogida", agencia.Tipo, "el tipo y el código se dan igual");
            Assert.AreEqual("NR930500", agencia.CodigoDocumento);
            Assert.AreEqual(AccionParadaNotaEntrega.NoAplica, apagado.Accion);
            Assert.AreEqual(0, paradas.Creados.Count);
        }

        [TestMethod]
        public async Task Parada_Ruta01_SoloCuandoElParametroLaIncluye()
        {
            Assert.AreEqual(AccionParadaNotaEntrega.NoAplica, (await servicioParadas.AsegurarParada(Pedido("01"), "alfredo")).Accion);

            RutasPropiasConfiguradas.Configurar(() => "AT,16,01,02,03,04,05");

            Assert.AreEqual(AccionParadaNotaEntrega.Creada, (await servicioParadas.AsegurarParada(Pedido("01"), "alfredo")).Accion);
        }

        [TestMethod]
        public async Task ResolverParada_CodigoDeLaNota_DaSuEnvioDelRepartoPropio()
        {
            paradas.Envios.Add(new EnvioPedidoNota { Numero = 251400, Agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO, Estado = Constantes.Agencias.ESTADO_TRAMITADO });

            Assert.AreEqual(251400, await servicioParadas.ResolverParada("NR930500-1"));
            Assert.AreEqual(251400, await servicioParadas.ResolverParada("ne930500"));
            Assert.IsNull(await servicioParadas.ResolverParada("NE930501"), "no es nota");
            Assert.IsNull(await servicioParadas.ResolverParada("RP251400-1"));
        }

        // ================================================================ etiquetas de recogida

        [TestMethod]
        public async Task Etiquetas_Recogida_UnaPorBultoConSuCodigoPorLaImpresoraDeAgenciaDelUsuario()
        {
            cabeceras.Filas[NOTA] = Fila("Recogida", 2);
            var impresora = new ImpresoraFalsa();
            var parametros = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => parametros.LeerParametro(EMPRESA, "alfredo", "ImpresoraAgencia")).Returns(@"\\RDS2016\Zebra");
            var servicio = new ServicioEtiquetasRecogida(cabeceras, paradas, parametros, impresora, () => new DateTime(2026, 10, 30));

            ResultadoEtiquetasRecogida r = await servicio.Imprimir(EMPRESA, NOTA, null, "NUEVAVISION\\alfredo");

            Assert.AreEqual(TipoResultadoNotaEntrega.Ok, r.Tipo);
            Assert.AreEqual(2, r.Contenido.Impresas);
            CollectionAssert.AreEqual(new[] { "NR930500-1", "NR930500-2" }, r.Contenido.Codigos);
            Assert.AreEqual(@"\\RDS2016\Zebra", impresora.Impresora);
            string zpl = impresora.Contenido;
            Assert.AreEqual(2, zpl.Split(new[] { "^XA" }, StringSplitOptions.None).Length - 1);
            StringAssert.Contains(zpl, "^FDRECOGIDA^FS");
            StringAssert.Contains(zpl, "^FDNR930500-1^FS");
            StringAssert.Contains(zpl, "^FDNR930500-2^FS");
            StringAssert.Contains(zpl, "Bulto 2/2");
            StringAssert.Contains(zpl, "Recoger el sillón");
            Assert.IsFalse(zpl.Contains("REEMBOLSO"));
        }

        [TestMethod]
        public async Task Etiquetas_NotaDeEntrega_400SinImprimir()
        {
            var impresora = new ImpresoraFalsa();
            var servicio = new ServicioEtiquetasRecogida(cabeceras, paradas, A.Fake<ILectorParametrosUsuario>(), impresora);

            ResultadoEtiquetasRecogida r = await servicio.Imprimir(EMPRESA, NOTA, "Zebra", "alfredo");

            Assert.AreEqual(TipoResultadoNotaEntrega.Invalido, r.Tipo);
            StringAssert.Contains(r.Mensaje, "etiqueta del packing");
            Assert.IsNull(impresora.Contenido);
        }

        [TestMethod]
        public async Task Etiquetas_PedidoQueNoEsNotaONoExiste_400Y404()
        {
            cabeceras.Pedidos[930501] = new PedidoNotaEntrega { Cliente = CLIENTE, NotaEntrega = false };
            var servicio = new ServicioEtiquetasRecogida(cabeceras, paradas, A.Fake<ILectorParametrosUsuario>(), new ImpresoraFalsa());

            Assert.AreEqual(TipoResultadoNotaEntrega.Invalido, (await servicio.Imprimir(EMPRESA, 930501, "Zebra", "alfredo")).Tipo);
            Assert.AreEqual(TipoResultadoNotaEntrega.NoEncontrado, (await servicio.Imprimir(EMPRESA, 999999, "Zebra", "alfredo")).Tipo);
        }

        [TestMethod]
        public async Task Etiquetas_FallaLaImpresora_FalloImpresora()
        {
            cabeceras.Filas[NOTA] = Fila("Recogida", 1);
            var servicio = new ServicioEtiquetasRecogida(cabeceras, paradas, A.Fake<ILectorParametrosUsuario>(), new ImpresoraFalsa { Fallar = true });

            ResultadoEtiquetasRecogida r = await servicio.Imprimir(EMPRESA, NOTA, "Zebra", "alfredo");

            Assert.AreEqual(TipoResultadoNotaEntrega.FalloImpresora, r.Tipo);
            StringAssert.Contains(r.Mensaje, "Zebra");
        }

        [TestMethod]
        public async Task Etiquetas_ConParada_UsaLosDatosDeSuEnvio()
        {
            cabeceras.Filas[NOTA] = Fila("Recogida", 1);
            paradas.Envios.Add(new EnvioPedidoNota { Numero = 251400, Agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO, Estado = Constantes.Agencias.ESTADO_TRAMITADO });
            paradas.EnviosCompletos[251400] = new EnviosAgencia { Numero = 251400, Pedido = NOTA, Nombre = "OTRA DIRECCIÓN DEL ENVÍO", Bultos = 1, Reembolso = 50m };
            var impresora = new ImpresoraFalsa();
            var servicio = new ServicioEtiquetasRecogida(cabeceras, paradas, A.Fake<ILectorParametrosUsuario>(), impresora);

            _ = await servicio.Imprimir(EMPRESA, NOTA, "Zebra", "alfredo");

            StringAssert.Contains(impresora.Contenido, "OTRA DIRECCIÓN DEL ENVÍO");
            Assert.IsFalse(impresora.Contenido.Contains("REEMBOLSO"), "una recogida nunca cobra");
        }

        [TestMethod]
        public void EtiquetaRepartoPropio_ConLaPlantillaComun_SaleIgualQueAntes()
        {
            var envio = new NestoAPI.Infraestructure.Agencias.DatosEnvioRemoto { NumeroEnvio = 251400, Referencia = "930123", Nombre = "CLIENTE", Bultos = 2 };

            string zpl = EtiquetaRepartoPropio.ConstruirZpl(envio, new DateTime(2026, 10, 30));

            StringAssert.Contains(zpl, "^FO300,25^A0N,48,44^FDREPARTO PROPIO^FS");
            StringAssert.Contains(zpl, "^FO300,85^A0N,32,30^FDPedido: 930123^FS");
            StringAssert.Contains(zpl, "^FDRP251400-2^FS");
            StringAssert.Contains(zpl, "^FO20,1110^A0N,28,26^FDNueva Visión - Envío 251400^FS");
        }

        // ================================================================ el documento (PDF)

        [TestMethod]
        public async Task Documento_PorTipo_TituloYCodigo()
        {
            var gestorFacturas = A.Fake<IGestorFacturas>();
            A.CallTo(() => gestorFacturas.LeerPedido(EMPRESA, NOTA)).ReturnsLazily(() => FacturaDeLaNota());
            cabeceras.NumeroNota = 45123;
            var servicio = new ServicioDocumentoNotaEntrega(gestorFacturas, cabeceras, paradas);

            Factura entrega = await servicio.LeerParaImprimir(EMPRESA, NOTA);
            cabeceras.Filas[NOTA] = Fila("Recogida", 3);
            Factura recogida = await servicio.LeerParaImprimir(EMPRESA, NOTA);

            Assert.AreEqual("NOTA DE ENTREGA", entrega.NotaEntrega.Titulo);
            Assert.AreEqual("NE930500", entrega.NotaEntrega.CodigoBarras);
            Assert.IsNull(entrega.NotaEntrega.Bultos, "sin envío del packing todavía");
            Assert.AreEqual("NOTA DE RECOGIDA", recogida.NotaEntrega.Titulo);
            Assert.AreEqual("NR930500", recogida.NotaEntrega.CodigoBarras);
            Assert.AreEqual((short)3, recogida.NotaEntrega.Bultos);
            Assert.AreEqual(45123, recogida.NotaEntrega.NumeroNota);
            Assert.AreEqual("915551234", recogida.NotaEntrega.Telefono);
            Assert.AreEqual("666123456", recogida.NotaEntrega.Movil);
            Assert.AreEqual("NOTA DE RECOGIDA", GeneradorPdfFacturasQuestPdf.TituloDocumento(recogida));
            StringAssert.Contains(GeneradorPdfFacturasQuestPdf.TextoDatosNotaEntrega(recogida.NotaEntrega), "Bultos: 3");
            StringAssert.Contains(GeneradorPdfFacturasQuestPdf.TextoDatosNotaEntrega(recogida.NotaEntrega), "Nº nota: 45123");
        }

        [TestMethod]
        public async Task Documento_EntregaConEnvio_LosBultosDelPacking()
        {
            var gestorFacturas = A.Fake<IGestorFacturas>();
            A.CallTo(() => gestorFacturas.LeerPedido(EMPRESA, NOTA)).ReturnsLazily(() => FacturaDeLaNota());
            paradas.Envios.Add(new EnvioPedidoNota { Numero = 251400, Agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO, Estado = Constantes.Agencias.ESTADO_TRAMITADO, Bultos = 4 });

            Factura entrega = await new ServicioDocumentoNotaEntrega(gestorFacturas, cabeceras, paradas).LeerParaImprimir(EMPRESA, NOTA);

            Assert.AreEqual((short)4, entrega.NotaEntrega.Bultos);
        }

        [TestMethod]
        public async Task Documento_NoEsNota_404YNoGeneraNada()
        {
            cabeceras.Pedidos[930501] = new PedidoNotaEntrega { Cliente = CLIENTE, NotaEntrega = false };
            var gestorFacturas = A.Fake<IGestorFacturas>();
            var servicio = new ServicioDocumentoNotaEntrega(gestorFacturas, cabeceras, paradas);

            Assert.AreEqual(TipoResultadoNotaEntrega.NoEncontrado, (await servicio.Generar(EMPRESA, 930501, "alfredo", false)).Tipo);
            Assert.AreEqual(TipoResultadoNotaEntrega.NoEncontrado, (await servicio.Generar(EMPRESA, 999999, "alfredo", false)).Tipo);
            A.CallTo(() => gestorFacturas.LeerPedido(A<string>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Documento_SiempreConQuestPdf()
        {
            var gestorFacturas = A.Fake<IGestorFacturas>();
            A.CallTo(() => gestorFacturas.LeerPedido(EMPRESA, NOTA)).ReturnsLazily(() => FacturaDeLaNota());
            A.CallTo(() => gestorFacturas.FacturasEnPDF(A<List<Factura>>._, A<bool>._, A<string>._, A<bool>._, A<bool>._))
                .Returns(new ByteArrayContent(new byte[] { 1, 2, 3 }));

            ResultadoDocumentoNotaEntrega r = await new ServicioDocumentoNotaEntrega(gestorFacturas, cabeceras, paradas).Generar(EMPRESA, NOTA, "alfredo", true);

            Assert.AreEqual(TipoResultadoNotaEntrega.Ok, r.Tipo);
            Assert.AreEqual("NE930500", r.CodigoDocumento);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, r.Pdf);
            A.CallTo(() => gestorFacturas.FacturasEnPDF(A<List<Factura>>.That.Matches(l => l.Single().NotaEntrega != null), true, "alfredo", false, true))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void GenerarPdf_NotaDeRecogida_DibujaElDocumentoConSuCodigo()
        {
            Factura factura = FacturaDeLaNota();
            factura.TipoDocumento = Constantes.Facturas.TiposDocumento.NOTA_ENTREGA;
            factura.NotaEntrega = new DatosNotaEntregaFactura
            {
                Tipo = "Recogida", Titulo = "NOTA DE RECOGIDA", CodigoBarras = "NR930500", NumeroNota = 45123, Bultos = 2, Telefono = "915551234"
            };

            byte[] pdf = new GeneradorPdfFacturasQuestPdf().GenerarPdf(new List<Factura> { factura }, papelConMembrete: true)
                .ReadAsByteArrayAsync().GetAwaiter().GetResult();

            Assert.IsTrue(pdf.Length > 1000);
            Assert.AreEqual("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        }

        // ================================================================ Facturar rutas

        [TestMethod]
        public async Task FacturarRutas_RecogidaDeRutaPropia_ParadaEtiquetasYCamposNuevosEnLaRespuesta()
        {
            (GestorFacturacionRutas gestor, IServicioNotasEntrega notas, IServicioParadasNotaEntrega paradasFake, IServicioEtiquetasRecogida etiquetas) = Gestor();
            CabPedidoVta pedido = Pedido("AT");
            A.CallTo(() => notas.ProcesarNotaEntrega(pedido, "alfredo")).Returns(new NotaEntregaCreadaDTO { NumeroPedido = NOTA, NumeroLineas = 1 });
            A.CallTo(() => paradasFake.AsegurarParada(pedido, "alfredo")).Returns(new ResultadoParadaNotaEntrega
            {
                Accion = AccionParadaNotaEntrega.Creada, Tipo = "Recogida", CodigoDocumento = "NR930500", Bultos = 3, EnvioId = 251400
            });
            A.CallTo(() => etiquetas.Imprimir(EMPRESA, NOTA, null, "alfredo"))
                .Returns(ResultadoEtiquetasRecogida.Ok(new EtiquetasRecogidaResultadoDTO { Impresas = 3 }));

            FacturarRutasResponseDTO respuesta = await gestor.FacturarRutas(new List<CabPedidoVta> { pedido }, "alfredo", DateTime.Today);

            NotaEntregaCreadaDTO nota = respuesta.NotasEntrega.Single();
            Assert.AreEqual("Recogida", nota.Tipo);
            Assert.AreEqual("NR930500", nota.CodigoDocumento);
            Assert.AreEqual((short)3, nota.Bultos);
            Assert.AreEqual(251400, nota.EnvioId);
            Assert.AreEqual(3, nota.EtiquetasRecogidaImpresas);
            Assert.AreEqual(0, respuesta.PedidosConErrores.Count);
        }

        [TestMethod]
        public async Task FacturarRutas_EntregaReutilizada_SinEtiquetasDeRecogida()
        {
            (GestorFacturacionRutas gestor, IServicioNotasEntrega notas, IServicioParadasNotaEntrega paradasFake, IServicioEtiquetasRecogida etiquetas) = Gestor();
            CabPedidoVta pedido = Pedido("16");
            A.CallTo(() => notas.ProcesarNotaEntrega(pedido, "alfredo")).Returns(new NotaEntregaCreadaDTO { NumeroPedido = NOTA });
            A.CallTo(() => paradasFake.AsegurarParada(pedido, "alfredo")).Returns(new ResultadoParadaNotaEntrega
            {
                Accion = AccionParadaNotaEntrega.Reutilizada, Tipo = "Entrega", CodigoDocumento = "NE930500", Bultos = 2, EnvioId = 251400
            });

            FacturarRutasResponseDTO respuesta = await gestor.FacturarRutas(new List<CabPedidoVta> { pedido }, "alfredo", DateTime.Today);

            Assert.AreEqual(251400, respuesta.NotasEntrega.Single().EnvioId);
            Assert.AreEqual(0, respuesta.NotasEntrega.Single().EtiquetasRecogidaImpresas);
            A.CallTo(() => etiquetas.Imprimir(A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task FacturarRutas_FallaLaImpresoraOLaParada_ErrorDelPedidoPeroLaNotaSigue()
        {
            (GestorFacturacionRutas gestor, IServicioNotasEntrega notas, IServicioParadasNotaEntrega paradasFake, IServicioEtiquetasRecogida etiquetas) = Gestor();
            CabPedidoVta recogida = Pedido("AT");
            CabPedidoVta otra = Pedido("AT");
            otra.Número = NOTA + 1;
            A.CallTo(() => notas.ProcesarNotaEntrega(A<CabPedidoVta>._, "alfredo")).ReturnsLazily((CabPedidoVta p, string u) => new NotaEntregaCreadaDTO { NumeroPedido = p.Número });
            A.CallTo(() => paradasFake.AsegurarParada(recogida, "alfredo")).Returns(new ResultadoParadaNotaEntrega
            {
                Accion = AccionParadaNotaEntrega.Creada, Tipo = "Recogida", CodigoDocumento = "NR930500", Bultos = 1, EnvioId = 251400
            });
            A.CallTo(() => paradasFake.AsegurarParada(otra, "alfredo")).Throws(new InvalidOperationException("se cayó la BD"));
            A.CallTo(() => etiquetas.Imprimir(EMPRESA, NOTA, null, "alfredo")).Returns(ResultadoEtiquetasRecogida.FalloImpresora("La Zebra no responde."));

            FacturarRutasResponseDTO respuesta = await gestor.FacturarRutas(new List<CabPedidoVta> { recogida, otra }, "alfredo", DateTime.Today);

            Assert.AreEqual(2, respuesta.NotasEntrega.Count);
            Assert.AreEqual(2, respuesta.PedidosConErrores.Count);
            Assert.IsTrue(respuesta.PedidosConErrores.Any(e => e.TipoError == "Etiquetas de recogida" && e.MensajeError.Contains("Zebra")));
            Assert.IsTrue(respuesta.PedidosConErrores.Any(e => e.TipoError == "Parada del reparto propio" && e.NumeroPedido == NOTA + 1));
        }

        // ================================================================ controlador

        [TestMethod]
        public async Task Controlador_Documento_PermisosYPdf()
        {
            var documentos = A.Fake<IServicioDocumentoNotaEntrega>();
            A.CallTo(() => documentos.Generar(EMPRESA, NOTA, "alfredo", false)).Returns(ResultadoDocumentoNotaEntrega.Ok(new byte[] { 37, 80, 68, 70 }, "NR930500"));
            A.CallTo(() => documentos.Generar(EMPRESA, 930501, A<string>._, A<bool>._)).Returns(ResultadoDocumentoNotaEntrega.NoEncontrado("no es nota"));

            HttpResponseMessage prohibido = await Ejecutar(await Controlador(Usuario("vendedor", "Ventas"), documentos).GetDocumento(EMPRESA, NOTA));
            HttpResponseMessage ok = await Ejecutar(await Controlador(Usuario("alfredo", Constantes.GruposSeguridad.ALMACEN), documentos).GetDocumento(EMPRESA, NOTA));
            HttpResponseMessage noEsNota = await Ejecutar(await Controlador(Usuario("alfredo", Constantes.GruposSeguridad.ALMACEN), documentos).GetDocumento(EMPRESA, 930501));

            Assert.AreEqual(HttpStatusCode.Forbidden, prohibido.StatusCode);
            Assert.AreEqual(HttpStatusCode.OK, ok.StatusCode);
            Assert.AreEqual("application/pdf", ok.Content.Headers.ContentType.MediaType);
            Assert.AreEqual("NR930500.pdf", ok.Content.Headers.ContentDisposition.FileName);
            Assert.AreEqual(HttpStatusCode.NotFound, noEsNota.StatusCode);
        }

        [TestMethod]
        public async Task Controlador_EtiquetasRecogida_200_400_502()
        {
            var etiquetas = A.Fake<IServicioEtiquetasRecogida>();
            A.CallTo(() => etiquetas.Imprimir(EMPRESA, NOTA, "Zebra", "alfredo"))
                .Returns(ResultadoEtiquetasRecogida.Ok(new EtiquetasRecogidaResultadoDTO { Impresas = 2, Codigos = new List<string> { "NR930500-1", "NR930500-2" } }));
            A.CallTo(() => etiquetas.Imprimir(EMPRESA, 930501, A<string>._, A<string>._)).Returns(ResultadoEtiquetasRecogida.Invalido("es de entrega"));
            A.CallTo(() => etiquetas.Imprimir(EMPRESA, 930502, A<string>._, A<string>._)).Returns(ResultadoEtiquetasRecogida.FalloImpresora("Zebra"));
            NotasEntregaController controlador = Controlador(Usuario("alfredo", Constantes.GruposSeguridad.ALMACEN), etiquetas: etiquetas);
            var peticion = new EtiquetasRecogidaPeticionDTO { Impresora = "Zebra" };

            var ok = await controlador.PostEtiquetasRecogida(EMPRESA, NOTA, peticion) as OkNegotiatedContentResult<EtiquetasRecogidaResultadoDTO>;
            HttpResponseMessage entrega = await Ejecutar(await controlador.PostEtiquetasRecogida(EMPRESA, 930501, peticion));
            HttpResponseMessage impresora = await Ejecutar(await controlador.PostEtiquetasRecogida(EMPRESA, 930502, peticion));
            HttpResponseMessage prohibido = await Ejecutar(await Controlador(Usuario("tienda", Constantes.GruposSeguridad.TIENDAS), etiquetas: etiquetas)
                .PostEtiquetasRecogida(EMPRESA, NOTA, peticion));

            Assert.IsNotNull(ok);
            Assert.AreEqual(2, ok.Content.Impresas);
            Assert.AreEqual(HttpStatusCode.BadRequest, entrega.StatusCode);
            Assert.AreEqual(HttpStatusCode.BadGateway, impresora.StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, prohibido.StatusCode);
        }

        // ================================================================ ayudantes

        private static FilaNotaEntregaCabecera Fila(string tipo, short bultos)
            => new FilaNotaEntregaCabecera { Empresa = EMPRESA, Pedido = NOTA, Tipo = tipo, Bultos = bultos };

        private static CabPedidoVta Pedido(string ruta) => new CabPedidoVta
        {
            Empresa = EMPRESA,
            Número = NOTA,
            Nº_Cliente = CLIENTE,
            Contacto = "0",
            Ruta = ruta,
            NotaEntrega = true,
            LinPedidoVtas = new List<LinPedidoVta>
            {
                new LinPedidoVta { Estado = Constantes.EstadosLineaVenta.EN_CURSO, Picking = 5, VtoBueno = true, TipoLinea = 0, Texto = "Recoger el sillón" }
            }
        };

        private static Factura FacturaDeLaNota() => new Factura
        {
            Cliente = CLIENTE,
            NumeroFactura = NOTA.ToString(),
            Fecha = new DateTime(2026, 10, 30),
            TipoDocumento = Constantes.Facturas.TiposDocumento.NOTA_ENTREGA,
            Direcciones = new List<DireccionFactura>
            {
                new DireccionFactura { Tipo = "Empresa", Nombre = "NUEVA VISIÓN" },
                new DireccionFactura { Tipo = "Fiscal", Nombre = "PELUQUERÍA LA BONITA" },
                new DireccionFactura { Tipo = "Entrega", Nombre = "PELUQUERÍA LA BONITA", Telefonos = "915551234/666123456" }
            },
            Lineas = new List<LineaFactura>
            {
                new LineaFactura { Producto = "12345", Descripcion = "SILLÓN DE CORTE", Cantidad = 1 },
                new LineaFactura { Descripcion = "Recoger el sillón y la lámpara" }
            },
            NotasAlPie = new List<NotaFactura>(),
            Totales = new List<TotalFactura>(),
            Vencimientos = new List<VencimientoFactura>(),
            Vendedores = new List<VendedorFactura>()
        };

        private static (GestorFacturacionRutas, IServicioNotasEntrega, IServicioParadasNotaEntrega, IServicioEtiquetasRecogida) Gestor()
        {
            NVEntities db = A.Fake<NVEntities>();
            var notas = A.Fake<IServicioNotasEntrega>();
            var paradasFake = A.Fake<IServicioParadasNotaEntrega>();
            var etiquetas = A.Fake<IServicioEtiquetasRecogida>();
            var documentos = A.Fake<IServicioDocumentoNotaEntrega>();
            A.CallTo(() => documentos.LeerParaImprimir(A<string>._, A<int>._)).Returns(Task.FromResult(new Factura()));
            A.CallTo(() => documentos.Pdf(A<Factura>._, A<bool>._, A<string>._)).Returns(new byte[] { 1 });
            var gestor = new GestorFacturacionRutas(db, A.Fake<IServicioAlbaranesVenta>(), A.Fake<IServicioFacturas>(), A.Fake<IGestorFacturas>(),
                A.Fake<IServicioTraspasoEmpresa>(), notas, A.Fake<IServicioExtractoRuta>())
            {
                ParadasNotaEntrega = paradasFake,
                EtiquetasRecogida = etiquetas,
                DocumentoNotaEntrega = documentos
            };
            return (gestor, notas, paradasFake, etiquetas);
        }

        private NotasEntregaController Controlador(IPrincipal usuario, IServicioDocumentoNotaEntrega documentos = null, IServicioEtiquetasRecogida etiquetas = null)
            => new NotasEntregaController(null, new ServicioNotasEntregaCabecera(cabeceras, registrados.Add))
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = usuario,
                Documentos = documentos ?? A.Fake<IServicioDocumentoNotaEntrega>(),
                EtiquetasRecogida = etiquetas ?? A.Fake<IServicioEtiquetasRecogida>()
            };

        private static Task<HttpResponseMessage> Ejecutar(IHttpActionResult resultado) => resultado.ExecuteAsync(CancellationToken.None);

        private static ClaimsPrincipal Usuario(string nombre, string grupo) => new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, nombre), new Claim(ClaimTypes.Role, grupo)
        }, "prueba"));

        private class ImpresoraFalsa : IImpresoraEtiquetas
        {
            public bool Fallar;
            public string Impresora;
            public string Contenido;

            public void Imprimir(string impresora, string contenido, string nombreTrabajo)
            {
                if (Fallar)
                {
                    throw new ImpresionEtiquetasException("Windows no deja abrir la impresora (5).");
                }
                Impresora = impresora;
                Contenido = contenido;
            }
        }

        private class RepositorioParadasFalso : IRepositorioParadasNotaEntrega
        {
            public readonly List<EnvioPedidoNota> Envios = new List<EnvioPedidoNota>();
            public readonly Dictionary<int, EnviosAgencia> EnviosCompletos = new Dictionary<int, EnviosAgencia>();
            public readonly Dictionary<int, DestinoNotaEntrega> Destinos = new Dictionary<int, DestinoNotaEntrega>();
            public readonly List<EnviosAgencia> Creados = new List<EnviosAgencia>();
            public readonly List<string> Historias = new List<string>();
            private int siguiente = 260000;

            public Task<List<EnvioPedidoNota>> LeerEnviosDelPedido(int pedido)
                => Task.FromResult(Envios.OrderByDescending(e => e.Numero).ToList());

            public Task<EnviosAgencia> LeerEnvio(int numero)
                => Task.FromResult(EnviosCompletos.TryGetValue(numero, out EnviosAgencia e) ? e : null);

            public Task<DestinoNotaEntrega> LeerDestinoDelPedido(string empresa, int pedido)
                => Task.FromResult(Destinos.TryGetValue(pedido, out DestinoNotaEntrega d) ? d : null);

            public Task<string> LeerEmpresaAgencia(int agencia) => Task.FromResult(EMPRESA);

            public Task<int> CrearEnvioTramitado(EnviosAgencia envio, string historia)
            {
                envio.Numero = siguiente++;
                envio.CodigoBarras = CodigoBultoRepartoPropio.Albaran(envio.Numero);
                Creados.Add(envio);
                Historias.Add(historia);
                Envios.Add(new EnvioPedidoNota { Numero = envio.Numero, Agencia = envio.Agencia, Estado = envio.Estado, Bultos = envio.Bultos });
                EnviosCompletos[envio.Numero] = envio;
                return Task.FromResult(envio.Numero);
            }
        }

        private class RepositorioCabecerasFalso : IRepositorioNotasEntregaCabecera
        {
            public readonly Dictionary<int, PedidoNotaEntrega> Pedidos = new Dictionary<int, PedidoNotaEntrega>();
            public readonly Dictionary<int, FilaNotaEntregaCabecera> Filas = new Dictionary<int, FilaNotaEntregaCabecera>();
            public int? NumeroNota;

            public bool ScriptAplicado() => true;
            public Task<FilaNotaEntregaCabecera> Leer(string empresa, int pedido)
                => Task.FromResult(Filas.TryGetValue(pedido, out FilaNotaEntregaCabecera f) ? f : null);
            public Task Guardar(FilaNotaEntregaCabecera fila, Modificacion modificacion) => Task.CompletedTask;
            public Task<PedidoNotaEntrega> LeerPedido(string empresa, int pedido)
                => Task.FromResult(Pedidos.TryGetValue(pedido, out PedidoNotaEntrega p) ? p : null);
            public Task<int?> LeerNumeroNota(string empresa, int pedido) => Task.FromResult(NumeroNota);
            public Task<bool> TieneLineasProcesadas(string empresa, int pedido) => Task.FromResult(false);
            public Task<EnvioNotaEntrega> LeerUltimoEnvio(string empresa, int pedido) => Task.FromResult<EnvioNotaEntrega>(null);
            public Task<string> LeerClienteFactura(string empresa, string factura) => Task.FromResult<string>(null);
        }
    }
}
