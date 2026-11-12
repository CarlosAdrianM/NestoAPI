using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;

namespace NestoAPI.Tests.Infrastructure.NotasEntrega
{
    /// <summary>
    /// NestoAPI#855: recogida solicitada por el vendedor. El vendedor la pide (recoge el Reparto, solo ruta propia, o el propio
    /// Vendedor), el almacén la confirma (nace la nota NR{pedido}) o la rechaza, quien la pidió la anula mientras no esté en reparto
    /// (después, Almacén o Administración) y, si lleva un día laborable sin contestar, se avisa una sola vez.
    /// </summary>
    [TestClass]
    public class RecogidasSolicitadasTests
    {
        // Jueves 12/11/26 a las 10:00
        private static readonly DateTime AHORA = new DateTime(2026, 11, 12, 10, 0, 0);
        private const string CLIENTE = "15191";
        private const string CLIENTE_AGENCIA = "20020";
        private const string VENDEDORA = "vendedora.app";
        private const string ALMACEN = "NUEVAVISION\\Alfredo";

        private RecogidasFalsas repositorio;
        private AvisosFalsos avisos;
        private DateTime ahora;
        private bool repartoActivo;
        private HashSet<DateTime> festivos;
        private ServicioRecogidasSolicitadas servicio;

        [TestInitialize]
        public void Preparar()
        {
            RutasPropiasConfiguradas.Reiniciar(); // AT y 16
            repositorio = new RecogidasFalsas();
            repositorio.Fichas[CLIENTE + "/0"] = new FichaNotaCobro { Nombre = "PELUQUERÍA DE PRUEBA", Ruta = "AT", Vendedor = "JE", Iva = "G21" };
            repositorio.Fichas[CLIENTE_AGENCIA + "/0"] = new FichaNotaCobro { Nombre = "ESTÉTICA DE PRUEBA", Ruta = "FW", Vendedor = "JE", Iva = "G21" };
            avisos = new AvisosFalsos();
            ahora = AHORA;
            repartoActivo = true;
            festivos = new HashSet<DateTime>();
            servicio = new ServicioRecogidasSolicitadas(repositorio, avisos, () => repartoActivo, () => ahora, d => festivos.Contains(d.Date));
        }

        [TestCleanup]
        public void Limpiar() => RutasPropiasConfiguradas.Reiniciar();

        private static UsuarioRecogidas Vendedora() => new UsuarioRecogidas { Nombre = VENDEDORA, Vendedor = "JE" };
        private static UsuarioRecogidas OtroVendedor() => new UsuarioRecogidas { Nombre = "otro.vendedor", Vendedor = "PA" };
        private static UsuarioRecogidas Almacen() => new UsuarioRecogidas { Nombre = ALMACEN, PuedeGestionar = true, PuedeAnularEnReparto = true };
        private static UsuarioRecogidas Informatica() => new UsuarioRecogidas { Nombre = "NUEVAVISION\\Carlos", PuedeGestionar = true };

        private static SolicitarRecogidaDTO Peticion(string cliente = CLIENTE, string quienRecoge = null, short? bultos = null, string texto = "Una caja de champú mal servida")
            => new SolicitarRecogidaDTO { Cliente = cliente, Contacto = "0", Texto = texto, Bultos = bultos, QuienRecoge = quienRecoge };

        private async Task<RecogidaSolicitadaDTO> Pedida(string cliente = CLIENTE, string quienRecoge = null)
        {
            ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO> r = await servicio.Solicitar(Peticion(cliente, quienRecoge), Vendedora());
            Assert.AreEqual(HttpStatusCode.Created, r.Codigo, r.Mensaje);
            return r.Contenido;
        }

        private async Task<RecogidaSolicitadaDTO> Confirmada(string cliente = CLIENTE, string quienRecoge = null)
        {
            RecogidaSolicitadaDTO pedida = await Pedida(cliente, quienRecoge);
            ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO> r = await servicio.Confirmar(pedida.Id, null, ALMACEN);
            Assert.AreEqual(HttpStatusCode.OK, r.Codigo, r.Mensaje);
            return r.Contenido;
        }

        // ================================================================ solicitar

        [TestMethod]
        public async Task Solicitar_RecogeElReparto_RutaPropia_201_Solicitada_YCampanaAAlmacen()
        {
            ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO> r = await servicio.Solicitar(Peticion(bultos: 2), Vendedora());

            Assert.AreEqual(HttpStatusCode.Created, r.Codigo, r.Mensaje);
            Assert.AreEqual(EstadosRecogidaSolicitada.SOLICITADA, r.Contenido.Estado);
            Assert.AreEqual(QuienRecogeRecogida.REPARTO, r.Contenido.QuienRecoge, "Por defecto recoge el reparto");
            Assert.AreEqual((short)2, r.Contenido.Bultos);
            Assert.AreEqual("JE", r.Contenido.Vendedor, "El vendedor del token");
            Assert.AreEqual(VENDEDORA, r.Contenido.UsuarioSolicita);
            Assert.AreEqual("PELUQUERÍA DE PRUEBA", r.Contenido.NombreCliente);
            Assert.IsNull(r.Contenido.CodigoDocumento, "Sin nota hasta que se confirme");
            Assert.AreEqual(1, repositorio.Filas.Count);
            Assert.AreEqual(1, avisos.Solicitadas.Count, "Campana a Almacén y haCambiado");
        }

        [TestMethod]
        public async Task Solicitar_RecogeElReparto_SinRutaPropia_400_YDiceQuePuedePedirlaComoLaRecojoYo()
        {
            ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO> r = await servicio.Solicitar(Peticion(CLIENTE_AGENCIA), Vendedora());

            Assert.AreEqual(HttpStatusCode.BadRequest, r.Codigo);
            StringAssert.Contains(r.Mensaje, "la recojo yo");
            StringAssert.Contains(r.Mensaje, "«FW»");
            Assert.AreEqual(0, repositorio.Filas.Count);
            Assert.AreEqual(0, avisos.Solicitadas.Count);
        }

        [TestMethod]
        public async Task Solicitar_RecogeElReparto_ConElRepartoPropioApagado_400()
        {
            repartoActivo = false;

            ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO> r = await servicio.Solicitar(Peticion(), Vendedora());

            Assert.AreEqual(HttpStatusCode.BadRequest, r.Codigo);
            StringAssert.Contains(r.Mensaje, "apagado");
        }

        [TestMethod]
        public async Task Solicitar_RecogeElVendedor_NoHaceFaltaRutaPropia_201()
        {
            repartoActivo = false;

            ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO> r = await servicio.Solicitar(Peticion(CLIENTE_AGENCIA, "vendedor"), Vendedora());

            Assert.AreEqual(HttpStatusCode.Created, r.Codigo, r.Mensaje);
            Assert.AreEqual(QuienRecogeRecogida.VENDEDOR, r.Contenido.QuienRecoge);
        }

        [TestMethod]
        public async Task Solicitar_QuienRecogeQueNoExiste_400()
        {
            ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO> r = await servicio.Solicitar(Peticion(quienRecoge: "Agencia"), Vendedora());

            Assert.AreEqual(HttpStatusCode.BadRequest, r.Codigo);
            StringAssert.Contains(r.Mensaje, "Agencia");
        }

        [TestMethod]
        public async Task Solicitar_Validaciones_400()
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, (await servicio.Solicitar(Peticion(texto: "  "), Vendedora())).Codigo, "Sin texto");
            Assert.AreEqual(HttpStatusCode.BadRequest, (await servicio.Solicitar(Peticion(texto: new string('x', 501)), Vendedora())).Codigo, "Texto largo");
            Assert.AreEqual(HttpStatusCode.BadRequest, (await servicio.Solicitar(Peticion(bultos: 0), Vendedora())).Codigo, "0 bultos");
            Assert.AreEqual(HttpStatusCode.BadRequest, (await servicio.Solicitar(Peticion(bultos: 1000), Vendedora())).Codigo, "1000 bultos");
            Assert.AreEqual(HttpStatusCode.BadRequest, (await servicio.Solicitar(new SolicitarRecogidaDTO { Cliente = CLIENTE, Texto = "x" }, Vendedora())).Codigo, "Sin contacto");
            Assert.AreEqual(HttpStatusCode.BadRequest, (await servicio.Solicitar(new SolicitarRecogidaDTO { Empresa = "3", Cliente = CLIENTE, Contacto = "0", Texto = "x" }, Vendedora())).Codigo, "Empresa 3");
            Assert.AreEqual(HttpStatusCode.NotFound, (await servicio.Solicitar(new SolicitarRecogidaDTO { Cliente = CLIENTE, Contacto = "9", Texto = "x" }, Vendedora())).Codigo, "Contacto que no existe");
            Assert.AreEqual(0, repositorio.Filas.Count);
        }

        [TestMethod]
        public async Task Solicitar_FacturaQueNoExisteODeOtroCliente_400_YLaSuya_201()
        {
            repositorio.Facturas["NV26/00100"] = CLIENTE;
            repositorio.Facturas["NV26/00200"] = "99999";

            SolicitarRecogidaDTO noExiste = Peticion();
            noExiste.FacturaOriginal = "NV26/00999";
            SolicitarRecogidaDTO deOtro = Peticion();
            deOtro.FacturaOriginal = "NV26/00200";
            SolicitarRecogidaDTO suya = Peticion();
            suya.FacturaOriginal = "nv26/00100";

            Assert.AreEqual(HttpStatusCode.BadRequest, (await servicio.Solicitar(noExiste, Vendedora())).Codigo);
            ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO> r = await servicio.Solicitar(deOtro, Vendedora());
            Assert.AreEqual(HttpStatusCode.BadRequest, r.Codigo);
            StringAssert.Contains(r.Mensaje, "99999");
            ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO> bien = await servicio.Solicitar(suya, Vendedora());
            Assert.AreEqual(HttpStatusCode.Created, bien.Codigo, bien.Mensaje);
            Assert.AreEqual("NV26/00100", bien.Contenido.FacturaOriginal);
        }

        [TestMethod]
        public async Task Solicitar_ElContactoYaTieneOtraSinContestar_409()
        {
            await Pedida();

            ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO> r = await servicio.Solicitar(Peticion(quienRecoge: "Vendedor"), Vendedora());

            Assert.AreEqual(HttpStatusCode.Conflict, r.Codigo);
            Assert.AreEqual(1, repositorio.Filas.Count);
        }

        [TestMethod]
        public async Task Solicitar_TrasConfirmarLaAnterior_SePuedePedirOtra()
        {
            await Confirmada();

            ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO> r = await servicio.Solicitar(Peticion(), Vendedora());

            Assert.AreEqual(HttpStatusCode.Created, r.Codigo, r.Mensaje);
        }

        // ================================================================ sin tabla

        [TestMethod]
        public async Task SinTabla_503_ConMensajeClaro_YElJobNoHaceNada()
        {
            repositorio.Script = false;

            ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO> r = await servicio.Solicitar(Peticion(), Vendedora());

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, r.Codigo);
            StringAssert.Contains(r.Mensaje, "Issue855_RecogidasSolicitadas.sql");
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (await servicio.LeerDelCliente(null, CLIENTE, null)).Codigo);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (await servicio.LeerPendientes(null)).Codigo);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (await servicio.Leer(1)).Codigo);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (await servicio.Confirmar(1, null, ALMACEN)).Codigo);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (await servicio.Rechazar(1, new RechazarRecogidaSolicitadaDTO { Motivo = "x" }, ALMACEN)).Codigo);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (await servicio.Anular(1, null, Vendedora())).Codigo);
            Assert.AreEqual(0, await servicio.AvisarSinRespuesta());
            Assert.AreEqual(0, repositorio.LecturasSinAvisar, "Sin tabla ni se consulta");
        }

        // ================================================================ confirmar

        [TestMethod]
        public async Task Confirmar_CreaLaNotaDeRecogida_ConLosBultosYElTextoCorregidos_YAvisaAQuienLaPidio()
        {
            RecogidaSolicitadaDTO pedida = await Pedida();
            string texto = "Dos cajas: una de champú de 5 litros con el tapón roto y otra de mascarillas que no pidió";

            ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO> r = await servicio.Confirmar(pedida.Id,
                new ConfirmarRecogidaSolicitadaDTO { Bultos = 2, Texto = texto }, ALMACEN);

            Assert.AreEqual(HttpStatusCode.OK, r.Codigo, r.Mensaje);
            Assert.AreEqual(EstadosRecogidaSolicitada.CONFIRMADA, r.Contenido.Estado);
            Assert.AreEqual(RecogidasFalsas.PEDIDO, r.Contenido.PedidoNota);
            Assert.AreEqual("NR" + RecogidasFalsas.PEDIDO, r.Contenido.CodigoDocumento);
            CollectionAssert.AreEqual(new[] { $"NR{RecogidasFalsas.PEDIDO}-1", $"NR{RecogidasFalsas.PEDIDO}-2" }, r.Contenido.CodigosBultos);
            Assert.AreEqual(ALMACEN, r.Contenido.UsuarioResuelve);
            Assert.AreEqual((short)2, repositorio.BultosConfirmados);
            Assert.AreEqual(texto, repositorio.TextoConfirmado);
            Assert.IsTrue(repositorio.LineasConfirmadas.All(l => l.Length <= 50), "LinPedidoVta.Texto es de 50");
            Assert.AreEqual(texto, string.Join(" ", repositorio.LineasConfirmadas));
            Assert.AreEqual(1, avisos.Resueltas.Count);
            Assert.AreEqual(EstadosRecogidaSolicitada.CONFIRMADA, avisos.Resueltas[0].Estado);
        }

        [TestMethod]
        public async Task Confirmar_YaResuelta_409_YSiOtroLaConfirmaALaVez_409()
        {
            RecogidaSolicitadaDTO pedida = await Pedida();
            Assert.AreEqual(HttpStatusCode.OK, (await servicio.Confirmar(pedida.Id, null, ALMACEN)).Codigo);

            ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO> otra = await servicio.Confirmar(pedida.Id, null, ALMACEN);
            Assert.AreEqual(HttpStatusCode.Conflict, otra.Codigo);
            StringAssert.Contains(otra.Mensaje, "ya está confirmada");

            RecogidaSolicitadaDTO segunda = await Pedida(CLIENTE_AGENCIA, QuienRecogeRecogida.VENDEDOR);
            repositorio.OtroConfirmaAntes = true;
            Assert.AreEqual(HttpStatusCode.Conflict, (await servicio.Confirmar(segunda.Id, null, ALMACEN)).Codigo);
            Assert.AreEqual(1, avisos.Resueltas.Count, "Solo avisa la que de verdad se ha confirmado");
            Assert.AreEqual(HttpStatusCode.NotFound, (await servicio.Confirmar(999, null, ALMACEN)).Codigo);
        }

        [TestMethod]
        public async Task Confirmar_RecogeElReparto_YElContactoYaNoEsDeRutaPropia_400_YRecogeElVendedor_Vale()
        {
            RecogidaSolicitadaDTO reparto = await Pedida();
            repositorio.Fichas[CLIENTE + "/0"].Ruta = "FW";

            Assert.AreEqual(HttpStatusCode.BadRequest, (await servicio.Confirmar(reparto.Id, null, ALMACEN)).Codigo);

            RecogidaSolicitadaDTO vendedor = await Pedida(CLIENTE_AGENCIA, QuienRecogeRecogida.VENDEDOR);
            ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO> r = await servicio.Confirmar(vendedor.Id, null, ALMACEN);
            Assert.AreEqual(HttpStatusCode.OK, r.Codigo, r.Mensaje);
            Assert.AreEqual(QuienRecogeRecogida.VENDEDOR, repositorio.QuienRecogeConfirmado, "El repositorio sabe que no va en el reparto");
        }

        [TestMethod]
        public void ConstruirCabecera_NotaDeRecogida_SinFacturar_ConElComentario()
        {
            var fila = new FilaRecogidaSolicitada { Id = 7, Empresa = "1", Cliente = CLIENTE, Contacto = "0", UsuarioSolicita = VENDEDORA, QuienRecoge = QuienRecogeRecogida.VENDEDOR };
            CabPedidoVta cabecera = RepositorioRecogidasSolicitadas.ConstruirCabecera(fila, 930500, "Una caja", new FichaNotaCobro { Ruta = "FW", Iva = "G21", Vendedor = "JE" },
                AHORA, "NUEVAVISION\\Alfredo");

            Assert.IsTrue(cabecera.NotaEntrega);
            Assert.AreEqual(930500, cabecera.Número);
            Assert.AreEqual("FW", cabecera.Ruta);
            Assert.AreEqual(Constantes.PlazosPago.CONTADO, cabecera.PlazosPago);
            Assert.AreEqual(Constantes.Pedidos.PERIODO_FACTURACION_NORMAL, cabecera.Periodo_Facturacion);
            StringAssert.Contains(cabecera.Comentarios, "solicitud 7");
            StringAssert.Contains(cabecera.Comentarios, "LA RECOGE EL VENDEDOR");

            LineaPedidoVentaDTO linea = RepositorioRecogidasSolicitadas.LineaTexto("Una caja", AHORA, "NUEVAVISION\\Alfredo");
            Assert.AreEqual((byte)Constantes.TiposLineaVenta.TEXTO, linea.tipoLinea);
            Assert.AreEqual(Constantes.EstadosLineaVenta.EN_CURSO, linea.estado);
            Assert.AreEqual(Constantes.Almacenes.ALGETE, linea.almacen);
        }

        // ================================================================ rechazar

        [TestMethod]
        public async Task Rechazar_SinMotivo_400_ConMotivo_Rechazada_YAvisa_YOtraVez_409()
        {
            RecogidaSolicitadaDTO pedida = await Pedida();

            Assert.AreEqual(HttpStatusCode.BadRequest, (await servicio.Rechazar(pedida.Id, new RechazarRecogidaSolicitadaDTO { Motivo = " " }, ALMACEN)).Codigo);
            ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO> r = await servicio.Rechazar(pedida.Id, new RechazarRecogidaSolicitadaDTO { Motivo = "Ya lo trajo ella" }, ALMACEN);

            Assert.AreEqual(HttpStatusCode.OK, r.Codigo, r.Mensaje);
            Assert.AreEqual(EstadosRecogidaSolicitada.RECHAZADA, r.Contenido.Estado);
            Assert.AreEqual("Ya lo trajo ella", r.Contenido.MotivoRechazo);
            Assert.AreEqual(1, avisos.Resueltas.Count);
            Assert.AreEqual(EstadosRecogidaSolicitada.RECHAZADA, avisos.Resueltas[0].Estado);
            Assert.AreEqual(HttpStatusCode.Conflict, (await servicio.Rechazar(pedida.Id, new RechazarRecogidaSolicitadaDTO { Motivo = "x" }, ALMACEN)).Codigo);
            Assert.AreEqual(HttpStatusCode.Conflict, (await servicio.Confirmar(pedida.Id, null, ALMACEN)).Codigo);
        }

        // ================================================================ estados calculados

        [TestMethod]
        public void EstadoCalculado_DeLaNota()
        {
            var fila = new FilaRecogidaSolicitada { Estado = EstadosRecogidaSolicitada.CONFIRMADA, PedidoNota = 930500 };

            Assert.AreEqual(EstadosRecogidaSolicitada.CONFIRMADA, ServicioRecogidasSolicitadas.EstadoCalculado(fila, new SeguimientoNotaRecogida { Existe = true }));
            Assert.AreEqual(EstadosRecogidaSolicitada.CONFIRMADA, ServicioRecogidasSolicitadas.EstadoCalculado(fila,
                new SeguimientoNotaRecogida { Existe = true, Empezada = true }), "Con picking pero aún sin parada");
            Assert.AreEqual(EstadosRecogidaSolicitada.EN_REPARTO, ServicioRecogidasSolicitadas.EstadoCalculado(fila,
                new SeguimientoNotaRecogida { Existe = true, EstadoParada = Constantes.Agencias.ESTADO_TRAMITADO }));
            Assert.AreEqual(EstadosRecogidaSolicitada.EN_REPARTO, ServicioRecogidasSolicitadas.EstadoCalculado(fila,
                new SeguimientoNotaRecogida { Existe = true, EstadoParada = Constantes.Agencias.ESTADO_INCIDENTADO }));
            Assert.AreEqual(EstadosRecogidaSolicitada.RECOGIDA, ServicioRecogidasSolicitadas.EstadoCalculado(fila,
                new SeguimientoNotaRecogida { Existe = true, EstadoParada = Constantes.Agencias.ESTADO_ENTREGADO }));
            Assert.AreEqual(EstadosRecogidaSolicitada.RECOGIDA, ServicioRecogidasSolicitadas.EstadoCalculado(fila,
                new SeguimientoNotaRecogida { Existe = true, EstadoParada = Constantes.Agencias.ESTADO_TRAMITADO, BultosRecogidos = true }), "Bultos NR recogidos");
            Assert.AreEqual(EstadosRecogidaSolicitada.RECOGIDA, ServicioRecogidasSolicitadas.EstadoCalculado(fila,
                new SeguimientoNotaRecogida { Existe = true, PedidoAbono = 930600 }), "Leída en Algete (abono), también sin parada (la del vendedor)");

            fila.Estado = EstadosRecogidaSolicitada.RECHAZADA;
            Assert.AreEqual(EstadosRecogidaSolicitada.RECHAZADA, ServicioRecogidasSolicitadas.EstadoCalculado(fila, new SeguimientoNotaRecogida { PedidoAbono = 1 }));
        }

        [TestMethod]
        public async Task LeerDelCliente_ConElEstadoCalculado_DeLaMasRecienteALaMasAntigua()
        {
            RecogidaSolicitadaDTO primera = await Confirmada();
            ahora = AHORA.AddMinutes(5);
            RecogidaSolicitadaDTO segunda = await Pedida();
            repositorio.Seguimientos[RecogidasFalsas.PEDIDO] = new SeguimientoNotaRecogida { Existe = true, EstadoParada = Constantes.Agencias.ESTADO_TRAMITADO };

            ResultadoRecogidaSolicitada<List<RecogidaSolicitadaDTO>> r = await servicio.LeerDelCliente(null, CLIENTE, "0");

            Assert.AreEqual(HttpStatusCode.OK, r.Codigo);
            CollectionAssert.AreEqual(new[] { segunda.Id, primera.Id }, r.Contenido.Select(x => x.Id).ToArray());
            Assert.AreEqual(EstadosRecogidaSolicitada.SOLICITADA, r.Contenido[0].Estado);
            Assert.AreEqual(EstadosRecogidaSolicitada.EN_REPARTO, r.Contenido[1].Estado);
            Assert.AreEqual("PELUQUERÍA DE PRUEBA", r.Contenido[1].NombreCliente);

            ResultadoRecogidaSolicitada<List<RecogidaSolicitadaDTO>> pendientes = await servicio.LeerPendientes(null);
            CollectionAssert.AreEqual(new[] { segunda.Id }, pendientes.Contenido.Select(x => x.Id).ToArray());
        }

        // ================================================================ anular

        [TestMethod]
        public async Task Anular_Solicitada_QuienLaPidio_200_SinNotaQueBorrar()
        {
            RecogidaSolicitadaDTO pedida = await Pedida();

            ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO> r = await servicio.Anular(pedida.Id, "Me he equivocado de cliente", Vendedora());

            Assert.AreEqual(HttpStatusCode.OK, r.Codigo, r.Mensaje);
            Assert.AreEqual(EstadosRecogidaSolicitada.ANULADA, r.Contenido.Estado);
            Assert.AreEqual("Me he equivocado de cliente", r.Contenido.MotivoAnulacion);
            Assert.IsNull(r.Contenido.AvisoAnulacion);
            Assert.AreEqual(0, repositorio.NotasBorradas.Count);
            Assert.IsFalse(avisos.Anuladas.Single().AvisarAlmacen);
            Assert.AreEqual(HttpStatusCode.Conflict, (await servicio.Anular(pedida.Id, null, Vendedora())).Codigo, "Ya anulada");
        }

        [TestMethod]
        public async Task Anular_Solicitada_OtroVendedor_403_YElAlmacen_200()
        {
            RecogidaSolicitadaDTO pedida = await Pedida();

            Assert.AreEqual(HttpStatusCode.Forbidden, (await servicio.Anular(pedida.Id, null, OtroVendedor())).Codigo);
            Assert.AreEqual(HttpStatusCode.OK, (await servicio.Anular(pedida.Id, null, Informatica())).Codigo);
        }

        [TestMethod]
        public async Task Anular_Confirmada_SinEmpezar_QuienLaPidio_BorraLaNota()
        {
            RecogidaSolicitadaDTO confirmada = await Confirmada();
            repositorio.Seguimientos[RecogidasFalsas.PEDIDO] = new SeguimientoNotaRecogida { Existe = true };

            ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO> r = await servicio.Anular(confirmada.Id, null, Vendedora());

            Assert.AreEqual(HttpStatusCode.OK, r.Codigo, r.Mensaje);
            Assert.AreEqual(EstadosRecogidaSolicitada.ANULADA, r.Contenido.Estado);
            CollectionAssert.AreEqual(new[] { RecogidasFalsas.PEDIDO }, repositorio.NotasBorradas);
            Assert.IsNull(r.Contenido.AvisoAnulacion);
        }

        [TestMethod]
        public async Task Anular_Confirmada_EnReparto_QuienLaPidio_403_YAlmacen_200_SinBorrarLaNota_YAvisaAAlmacen()
        {
            RecogidaSolicitadaDTO confirmada = await Confirmada();
            repositorio.Seguimientos[RecogidasFalsas.PEDIDO] = new SeguimientoNotaRecogida
            {
                Existe = true, Empezada = true, TieneEnvio = true, EstadoParada = Constantes.Agencias.ESTADO_TRAMITADO
            };

            ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO> vendedora = await servicio.Anular(confirmada.Id, null, Vendedora());
            Assert.AreEqual(HttpStatusCode.Forbidden, vendedora.Codigo);
            StringAssert.Contains(vendedora.Mensaje, "Almacén o Administración");
            Assert.AreEqual(HttpStatusCode.Forbidden, (await servicio.Anular(confirmada.Id, null, Informatica())).Codigo,
                "Informática gestiona, pero la anulación en reparto es solo de Almacén y Administración");

            ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO> r = await servicio.Anular(confirmada.Id, "El cliente ya no la quiere devolver", Almacen());

            Assert.AreEqual(HttpStatusCode.OK, r.Codigo, r.Mensaje);
            Assert.AreEqual(EstadosRecogidaSolicitada.ANULADA, r.Contenido.Estado);
            Assert.AreEqual(0, repositorio.NotasBorradas.Count, "Con parada no se toca la nota");
            StringAssert.Contains(r.Contenido.AvisoAnulacion, "NR" + RecogidasFalsas.PEDIDO);
            Assert.IsTrue(avisos.Anuladas.Single().AvisarAlmacen);
        }

        [TestMethod]
        public async Task Anular_Confirmada_ConPickingSinParada_EsEnReparto_SoloAlmacen()
        {
            RecogidaSolicitadaDTO confirmada = await Confirmada();
            repositorio.Seguimientos[RecogidasFalsas.PEDIDO] = new SeguimientoNotaRecogida { Existe = true, Empezada = true };

            Assert.AreEqual(HttpStatusCode.Forbidden, (await servicio.Anular(confirmada.Id, null, Vendedora())).Codigo);
            Assert.AreEqual(HttpStatusCode.OK, (await servicio.Anular(confirmada.Id, null, Almacen())).Codigo);
            Assert.AreEqual(0, repositorio.NotasBorradas.Count);
        }

        [TestMethod]
        public async Task Anular_Confirmada_SiEntreMediasEntraEnUnPicking_NoSeBorra_YQuienLaPidio_403()
        {
            RecogidaSolicitadaDTO confirmada = await Confirmada();
            repositorio.Seguimientos[RecogidasFalsas.PEDIDO] = new SeguimientoNotaRecogida { Existe = true };
            repositorio.NotaEmpiezaAlBorrar = true;

            ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO> r = await servicio.Anular(confirmada.Id, null, Vendedora());

            Assert.AreEqual(HttpStatusCode.Forbidden, r.Codigo);
            Assert.AreEqual(0, repositorio.NotasBorradas.Count);
            Assert.AreEqual(EstadosRecogidaSolicitada.CONFIRMADA, repositorio.Filas[0].Estado);
        }

        [TestMethod]
        public async Task Anular_Recogida_409_ParaTodos()
        {
            RecogidaSolicitadaDTO confirmada = await Confirmada(CLIENTE_AGENCIA, QuienRecogeRecogida.VENDEDOR);
            repositorio.Seguimientos[RecogidasFalsas.PEDIDO] = new SeguimientoNotaRecogida { Existe = true, PedidoAbono = 930600 };

            Assert.AreEqual(HttpStatusCode.Conflict, (await servicio.Anular(confirmada.Id, null, Vendedora())).Codigo);
            ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO> r = await servicio.Anular(confirmada.Id, null, Almacen());
            Assert.AreEqual(HttpStatusCode.Conflict, r.Codigo);
            StringAssert.Contains(r.Mensaje, "ya está recogida");
        }

        [TestMethod]
        public async Task Anular_RecogeElVendedor_MientrasNoSeLeaEnAlgete_LaAnulaQuienLaPidio()
        {
            RecogidaSolicitadaDTO confirmada = await Confirmada(CLIENTE_AGENCIA, QuienRecogeRecogida.VENDEDOR);
            repositorio.Seguimientos[RecogidasFalsas.PEDIDO] = new SeguimientoNotaRecogida { Existe = true };

            ResultadoRecogidaSolicitada<RecogidaSolicitadaDTO> r = await servicio.Anular(confirmada.Id, null, Vendedora());

            Assert.AreEqual(HttpStatusCode.OK, r.Codigo, r.Mensaje);
            CollectionAssert.AreEqual(new[] { RecogidasFalsas.PEDIDO }, repositorio.NotasBorradas);
        }

        [TestMethod]
        public async Task Anular_Rechazada_409_YQueNoExiste_404()
        {
            RecogidaSolicitadaDTO pedida = await Pedida();
            await servicio.Rechazar(pedida.Id, new RechazarRecogidaSolicitadaDTO { Motivo = "No" }, ALMACEN);

            Assert.AreEqual(HttpStatusCode.Conflict, (await servicio.Anular(pedida.Id, null, Vendedora())).Codigo);
            Assert.AreEqual(HttpStatusCode.NotFound, (await servicio.Anular(999, null, Vendedora())).Codigo);
        }

        // ================================================================ un día laborable sin respuesta

        [TestMethod]
        public void VenceSinRespuesta_UnDiaLaborable()
        {
            Func<DateTime, bool> sinFestivos = d => false;
            Assert.AreEqual(new DateTime(2026, 11, 12, 10, 0, 0), ServicioRecogidasSolicitadas.VenceSinRespuesta(new DateTime(2026, 11, 11, 10, 0, 0), sinFestivos),
                "Miércoles → jueves a la misma hora");
            Assert.AreEqual(new DateTime(2026, 11, 16, 10, 0, 0), ServicioRecogidasSolicitadas.VenceSinRespuesta(new DateTime(2026, 11, 13, 10, 0, 0), sinFestivos),
                "Viernes → lunes: el fin de semana no cuenta");
            Assert.AreEqual(new DateTime(2026, 11, 17), ServicioRecogidasSolicitadas.VenceSinRespuesta(new DateTime(2026, 11, 14, 18, 0, 0), sinFestivos),
                "Sábado → cuenta el lunes entero");
            Func<DateTime, bool> juevesFestivo = d => d.Date == new DateTime(2026, 11, 12);
            Assert.AreEqual(new DateTime(2026, 11, 13, 10, 0, 0), ServicioRecogidasSolicitadas.VenceSinRespuesta(new DateTime(2026, 11, 11, 10, 0, 0), juevesFestivo),
                "Un festivo no cuenta");
        }

        [TestMethod]
        public async Task AvisarSinRespuesta_AlDiaLaborable_UnaSolaVez_YSinContarFestivosNiFinesDeSemana()
        {
            // Pedida el viernes 13/11 a las 12:00; el lunes 16 es festivo
            ahora = new DateTime(2026, 11, 13, 12, 0, 0);
            RecogidaSolicitadaDTO pedida = await Pedida();
            festivos.Add(new DateTime(2026, 11, 16));

            ahora = new DateTime(2026, 11, 16, 13, 0, 0);
            Assert.AreEqual(0, await servicio.AvisarSinRespuesta(), "El lunes es festivo: aún no");
            ahora = new DateTime(2026, 11, 17, 11, 59, 0);
            Assert.AreEqual(0, await servicio.AvisarSinRespuesta(), "El martes antes de las 12:00, aún no");

            ahora = new DateTime(2026, 11, 17, 12, 0, 0);
            Assert.AreEqual(1, await servicio.AvisarSinRespuesta());
            Assert.AreEqual(pedida.Id, avisos.SinRespuesta.Single().Id);

            ahora = new DateTime(2026, 11, 17, 13, 0, 0);
            Assert.AreEqual(0, await servicio.AvisarSinRespuesta(), "Una sola vez por solicitud");
            Assert.AreEqual(1, avisos.SinRespuesta.Count);
        }

        [TestMethod]
        public async Task AvisarSinRespuesta_LasContestadas_NoSeAvisan()
        {
            RecogidaSolicitadaDTO pedida = await Pedida();
            await servicio.Confirmar(pedida.Id, null, ALMACEN);

            ahora = AHORA.AddDays(7);
            Assert.AreEqual(0, await servicio.AvisarSinRespuesta());
        }

        // ================================================================ reglas puras

        [TestMethod]
        public void LineasTexto_PorPalabras_DeComoMucho50()
        {
            List<string> lineas = ServicioRecogidasSolicitadas.LineasTexto("Primera línea\r\n" + new string('a', 60) + " fin");

            CollectionAssert.AreEqual(new[] { "Primera línea", new string('a', 50), new string('a', 10) + " fin" }, lineas);
        }

        [TestMethod]
        public void QuienRecoge_Normalizar()
        {
            Assert.AreEqual(QuienRecogeRecogida.REPARTO, QuienRecogeRecogida.Normalizar(null));
            Assert.AreEqual(QuienRecogeRecogida.REPARTO, QuienRecogeRecogida.Normalizar(" reparto "));
            Assert.AreEqual(QuienRecogeRecogida.VENDEDOR, QuienRecogeRecogida.Normalizar("VENDEDOR"));
            Assert.IsNull(QuienRecogeRecogida.Normalizar("Agencia"));
        }

        [TestMethod]
        public void AlmacenHub_GrupoRecogidasSolicitadas()
        {
            Assert.AreEqual(AvisosAlmacen.GRUPO_RECOGIDAS_SOLICITADAS, AvisosAlmacen.Normalizar("recogidassolicitadas"));
        }

        // ================================================================ permisos

        private static ClaimsPrincipal Principal(params Claim[] claims)
            => new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));

        private static ClaimsPrincipal VendedorApp(string vendedor = "JE")
            => Principal(new Claim(ClaimTypes.Name, VENDEDORA), new Claim("IsVendedor", "true"), new Claim("Vendedor", vendedor));

        [TestMethod]
        public void Permisos_VendedorSoloSusClientes_EmpleadoTodos_ClienteDeLaTiendaNinguno()
        {
            Func<string, string, string, string, bool> esDelVendedor = (empresa, usuario, vendedor, cliente) => vendedor == "JE" && cliente == CLIENTE;

            Assert.IsTrue(PermisosRecogidasSolicitadas.PuedeCliente(VendedorApp(), null, CLIENTE, esDelVendedor));
            Assert.IsFalse(PermisosRecogidasSolicitadas.PuedeCliente(VendedorApp(), null, CLIENTE_AGENCIA, esDelVendedor));
            Assert.IsFalse(PermisosRecogidasSolicitadas.PuedeCliente(VendedorApp("PA"), null, CLIENTE, esDelVendedor));
            Assert.IsTrue(PermisosRecogidasSolicitadas.PuedeCliente(Principal(new Claim(ClaimTypes.Name, "NUEVAVISION\\Laura"), new Claim("IsEmployee", "true")),
                null, CLIENTE_AGENCIA, esDelVendedor));
            Assert.IsFalse(PermisosRecogidasSolicitadas.PuedeCliente(Principal(new Claim(ClaimTypes.Name, "cliente@prueba.es"), new Claim("cliente", CLIENTE)),
                null, CLIENTE, esDelVendedor), "Un cliente de la tienda no pide recogidas");
            Assert.IsTrue(PermisosRecogidasSolicitadas.PuedeAnularEnReparto(Principal(new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.ADMINISTRACION))));
            Assert.IsFalse(PermisosRecogidasSolicitadas.PuedeAnularEnReparto(Principal(new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.DIRECCION))));
        }

        private NotasEntregaController Controlador(ClaimsPrincipal usuario) => new NotasEntregaController(null, A.Fake<IServicioNotasEntregaCabecera>())
        {
            Request = new HttpRequestMessage(),
            Configuration = new HttpConfiguration(),
            User = usuario,
            RecogidasSolicitadas = servicio,
            EsClienteDelVendedor = (empresa, usuario2, vendedor, cliente) => cliente == CLIENTE
        };

        private static async Task<HttpStatusCode> Codigo(IHttpActionResult resultado)
            => (await resultado.ExecuteAsync(CancellationToken.None)).StatusCode;

        [TestMethod]
        public async Task Controlador_VendedorConUnClienteQueNoEsSuyo_403_YConElSuyo_201()
        {
            NotasEntregaController controlador = Controlador(VendedorApp());

            Assert.AreEqual(HttpStatusCode.Forbidden, await Codigo(await controlador.PostRecogidaSolicitada(Peticion(CLIENTE_AGENCIA, "Vendedor"))));
            Assert.AreEqual(HttpStatusCode.Created, await Codigo(await controlador.PostRecogidaSolicitada(Peticion())));
            Assert.AreEqual(VENDEDORA, repositorio.Filas.Single().UsuarioSolicita);
            Assert.AreEqual("JE", repositorio.Filas.Single().Vendedor);
            Assert.AreEqual(HttpStatusCode.Forbidden, await Codigo(await controlador.GetRecogidasSolicitadas(CLIENTE_AGENCIA)));
            Assert.AreEqual(HttpStatusCode.OK, await Codigo(await controlador.GetRecogidasSolicitadas(CLIENTE)));
        }

        [TestMethod]
        public async Task Controlador_ConfirmarRechazarYPendientes_SoloLosGruposDeNotas()
        {
            RecogidaSolicitadaDTO pedida = await Pedida();
            NotasEntregaController vendedora = Controlador(VendedorApp());
            NotasEntregaController almacen = Controlador(Principal(new Claim(ClaimTypes.Name, ALMACEN), new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.ALMACEN)));

            Assert.AreEqual(HttpStatusCode.Forbidden, await Codigo(await vendedora.PostConfirmarRecogidaSolicitada(pedida.Id, null)));
            Assert.AreEqual(HttpStatusCode.Forbidden, await Codigo(await vendedora.PostRechazarRecogidaSolicitada(pedida.Id, new RechazarRecogidaSolicitadaDTO { Motivo = "x" })));
            Assert.AreEqual(HttpStatusCode.Forbidden, await Codigo(await vendedora.GetRecogidasSolicitadasPendientes()));
            Assert.AreEqual(HttpStatusCode.OK, await Codigo(await almacen.GetRecogidasSolicitadasPendientes()));
            Assert.AreEqual(HttpStatusCode.OK, await Codigo(await almacen.PostConfirmarRecogidaSolicitada(pedida.Id, null)));
            Assert.AreEqual(HttpStatusCode.Conflict, await Codigo(await almacen.PostRechazarRecogidaSolicitada(pedida.Id, new RechazarRecogidaSolicitadaDTO { Motivo = "x" })));
            Assert.AreEqual(HttpStatusCode.OK, await Codigo(await vendedora.GetRecogidaSolicitada(pedida.Id)), "Quien la pidió la ve");
            Assert.AreEqual(HttpStatusCode.NotFound, await Codigo(await almacen.GetRecogidaSolicitada(999)));
        }

        [TestMethod]
        public async Task Controlador_SinTabla_503()
        {
            repositorio.Script = false;

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, await Codigo(await Controlador(VendedorApp()).PostRecogidaSolicitada(Peticion())));
        }

        // ================================================================ el cierre en Algete (abono) de la que recoge el vendedor

        [TestMethod]
        public async Task Abono_RecogidaDelVendedorSinParada_SeCreaSinForzar_YLasDemasSiguenPidiendoLaParada()
        {
            IRepositorioAbonosRecogida abonos = A.Fake<IRepositorioAbonosRecogida>();
            A.CallTo(() => abonos.LeerNota(A<string>._, A<int>._)).ReturnsLazily((string e, int p) => Task.FromResult(new NotaRecogidaLeida
            {
                Empresa = "1", Pedido = p, NotaEntrega = true, Cliente = CLIENTE, Contacto = "0", Serie = "NV", Tipo = TiposNotaEntrega.RECOGIDA, Bultos = 1
            }));
            A.CallTo(() => abonos.LeerParada(A<string>._, A<int>._)).Returns(Task.FromResult<ParadaRecogidaLeida>(null));
            A.CallTo(() => abonos.LeerFicha(A<string>._, A<string>._, A<string>._)).Returns(Task.FromResult(new FichaClienteAbono { Iva = "G21", Ruta = "FW" }));
            A.CallTo(() => abonos.ReservarNumeroPedido()).Returns(930900);
            A.CallTo(() => abonos.GuardarAbono(A<CabPedidoVta>._, A<Modificacion>._, A<int>._, A<int?>._, A<string>._)).Returns(Task.FromResult(true));
            var servicioAbonos = new ServicioAbonosRecogida(abonos, () => AHORA, ex => { })
            {
                EsRecogidaDelVendedor = (empresa, pedido) => Task.FromResult(pedido == 930500)
            };

            ResultadoAbonoRecogida<AbonoRecogidaDTO> delVendedor = await servicioAbonos.CrearAbono(new CrearAbonoRecogidaDTO { Codigo = "NR930500" }, ALMACEN);
            ResultadoAbonoRecogida<AbonoRecogidaDTO> otra = await servicioAbonos.CrearAbono(new CrearAbonoRecogidaDTO { Codigo = "NR930501-1" }, ALMACEN);

            Assert.AreEqual(TipoResultadoNotaEntrega.Ok, delVendedor.Tipo, delVendedor.Mensaje);
            Assert.IsTrue(delVendedor.Creado);
            Assert.AreEqual(TipoResultadoNotaEntrega.Conflicto, otra.Tipo);
            Assert.AreEqual(ServicioAbonosRecogida.CODIGO_NO_RECOGIDA, otra.Codigo);
        }

        // ================================================================ avisos

        [TestMethod]
        public async Task Avisos_AQuienLaPidio_PushDeNestoApp_YCampanaDeNestoSiLaPidioDesdeNesto()
        {
            IServicioNotificacionesPush push = A.Fake<IServicioNotificacionesPush>();
            var reales = new AvisosRecogidasSolicitadas(() => push, n => Task.CompletedTask, (g, u) => { }, t => t());
            var recogida = new RecogidaSolicitadaDTO { Id = 5, Empresa = "1", Cliente = CLIENTE, Contacto = "0", UsuarioSolicita = "NUEVAVISION\\Laura", Estado = EstadosRecogidaSolicitada.CONFIRMADA };
            NotificacionPushDTO notificacion = AvisosRecogidasSolicitadas.Componer(AvisosRecogidasSolicitadas.TIPO_RESUELTA, recogida, "t", "c");

            await reales.AQuienLaPidio(recogida, notificacion);

            A.CallTo(() => push.GuardarEnBuzonDeUsuario("NUEVAVISION\\Laura", Constantes.Aplicaciones.NESTO, notificacion)).MustHaveHappenedOnceExactly();
            A.CallTo(() => push.EnviarAUsuario("Laura", Constantes.Aplicaciones.NESTO_APP, notificacion)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(AvisosRecogidasSolicitadas.TIPO_RESUELTA, notificacion.Datos["tipo"]);
            Assert.AreEqual("5", notificacion.Datos["id"]);
            Assert.AreEqual(CLIENTE, notificacion.Datos["cliente"]);
            Assert.AreEqual("0", notificacion.Datos["contacto"]);
            Assert.AreEqual("1", notificacion.Datos["empresa"]);
        }

        [TestMethod]
        public void Avisos_Solicitada_CampanaAAlmacen_YHaCambiado_YResuelta_LlevaElEstado()
        {
            var almacen = new List<NotificacionPushDTO>();
            var grupos = new List<string>();
            IServicioNotificacionesPush push = A.Fake<IServicioNotificacionesPush>();
            var reales = new AvisosRecogidasSolicitadas(() => push, n => { almacen.Add(n); return Task.CompletedTask; }, (g, u) => grupos.Add(g), t => t());
            var recogida = new RecogidaSolicitadaDTO
            {
                Id = 5, Empresa = "1", Cliente = CLIENTE, Contacto = "0", UsuarioSolicita = VENDEDORA, Texto = "Una caja", Bultos = 1,
                Estado = EstadosRecogidaSolicitada.RECHAZADA, MotivoRechazo = "No"
            };

            reales.Solicitada(recogida, VENDEDORA);
            reales.Resuelta(recogida, ALMACEN);

            Assert.AreEqual(AvisosRecogidasSolicitadas.TIPO_SOLICITADA, almacen.Single().Tipo);
            CollectionAssert.AreEqual(new[] { AvisosAlmacen.GRUPO_RECOGIDAS_SOLICITADAS, AvisosAlmacen.GRUPO_RECOGIDAS_SOLICITADAS }, grupos);
            A.CallTo(() => push.EnviarAUsuario(VENDEDORA, Constantes.Aplicaciones.NESTO_APP,
                A<NotificacionPushDTO>.That.Matches(n => n.Tipo == AvisosRecogidasSolicitadas.TIPO_RESUELTA && n.Datos["estado"] == EstadosRecogidaSolicitada.RECHAZADA)))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => push.GuardarEnBuzonDeUsuario(A<string>._, Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        // ================================================================ llega a Algete (remates de #855)

        [TestMethod]
        public async Task AlAbonar_LaDelVendedor_SeProcesaLaNota_YSeAvisaAQuienLaPidio()
        {
            RecogidaSolicitadaDTO confirmada = await Confirmada(CLIENTE_AGENCIA, QuienRecogeRecogida.VENDEDOR);
            repositorio.Seguimientos[RecogidasFalsas.PEDIDO] = new SeguimientoNotaRecogida { Existe = true, PedidoAbono = 930600 };

            await servicio.AlAbonar("1", RecogidasFalsas.PEDIDO, true, ALMACEN);

            Assert.AreEqual(RecogidasFalsas.PEDIDO, repositorio.NotasCerradas.Single());
            RecogidaSolicitadaDTO recibida = avisos.Recibidas.Single();
            Assert.AreEqual(confirmada.Id, recibida.Id);
            Assert.AreEqual(EstadosRecogidaSolicitada.RECOGIDA, recibida.Estado);
        }

        [TestMethod]
        public async Task AlAbonar_LaDelReparto_NoTocaLaNota_PeroAvisa()
        {
            _ = await Confirmada();

            await servicio.AlAbonar("1", RecogidasFalsas.PEDIDO, true, ALMACEN);

            Assert.AreEqual(0, repositorio.NotasCerradas.Count, "La nota del reparto sigue su camino de siempre (Facturar rutas)");
            Assert.AreEqual(1, avisos.Recibidas.Count);
        }

        [TestMethod]
        public async Task AlAbonar_NotaDeRecogidaSinSolicitud_NoHaceNada()
        {
            _ = await Confirmada(CLIENTE_AGENCIA, QuienRecogeRecogida.VENDEDOR);

            await servicio.AlAbonar("1", 929554, true, ALMACEN);

            Assert.AreEqual(0, repositorio.NotasCerradas.Count);
            Assert.AreEqual(0, avisos.Recibidas.Count);
        }

        [TestMethod]
        public async Task AlAbonar_AbonoYaExistente_ReintentaElCierre_PeroNoVuelveAAvisar()
        {
            _ = await Confirmada(CLIENTE_AGENCIA, QuienRecogeRecogida.VENDEDOR);

            await servicio.AlAbonar("1", RecogidasFalsas.PEDIDO, false, ALMACEN);

            Assert.AreEqual(1, repositorio.NotasCerradas.Count);
            Assert.AreEqual(0, avisos.Recibidas.Count);
        }

        [TestMethod]
        public async Task AlAbonar_SiFallaElCierre_NoLanza_YAvisaIgual()
        {
            _ = await Confirmada(CLIENTE_AGENCIA, QuienRecogeRecogida.VENDEDOR);
            repositorio.FallarAlCerrar = true;

            await servicio.AlAbonar("1", RecogidasFalsas.PEDIDO, true, ALMACEN);

            Assert.AreEqual(1, avisos.Recibidas.Count);
        }

        [TestMethod]
        public async Task AlAbonar_SinScript_NoHaceNada()
        {
            _ = await Confirmada(CLIENTE_AGENCIA, QuienRecogeRecogida.VENDEDOR);
            repositorio.Script = false;

            await servicio.AlAbonar("1", RecogidasFalsas.PEDIDO, true, ALMACEN);

            Assert.AreEqual(0, repositorio.NotasCerradas.Count);
            Assert.AreEqual(0, avisos.Recibidas.Count);
        }

        [TestMethod]
        public void Avisos_Recibida_PushYCampanaAQuienLaPidio_ConEstadoRecogida()
        {
            var grupos = new List<string>();
            IServicioNotificacionesPush push = A.Fake<IServicioNotificacionesPush>();
            var reales = new AvisosRecogidasSolicitadas(() => push, n => Task.CompletedTask, (g, u) => grupos.Add(g), t => t());
            var recogida = new RecogidaSolicitadaDTO
            {
                Id = 5, Empresa = "1", Cliente = CLIENTE, Contacto = "0", UsuarioSolicita = "NUEVAVISION\\Laura", Bultos = 2,
                Estado = EstadosRecogidaSolicitada.RECOGIDA, CodigoDocumento = "NR930500"
            };

            reales.Recibida(recogida, ALMACEN);

            CollectionAssert.AreEqual(new[] { AvisosAlmacen.GRUPO_RECOGIDAS_SOLICITADAS }, grupos);
            A.CallTo(() => push.EnviarAUsuario("Laura", Constantes.Aplicaciones.NESTO_APP,
                A<NotificacionPushDTO>.That.Matches(n => n.Tipo == AvisosRecogidasSolicitadas.TIPO_RECIBIDA && n.Datos["tipo"] == "RecogidaSolicitadaRecibida"
                    && n.Datos["estado"] == "Recogida" && n.Datos["id"] == "5" && n.Datos["cliente"] == CLIENTE && n.Datos["codigoDocumento"] == "NR930500")))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => push.GuardarEnBuzonDeUsuario("NUEVAVISION\\Laura", Constantes.Aplicaciones.NESTO,
                A<NotificacionPushDTO>.That.Matches(n => n.Tipo == AvisosRecogidasSolicitadas.TIPO_RECIBIDA))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Avisos_Recibida_SiLaPushFalla_NoLanza()
        {
            IServicioNotificacionesPush push = A.Fake<IServicioNotificacionesPush>();
            A.CallTo(() => push.EnviarAUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).Throws(new InvalidOperationException("sin red"));
            var reales = new AvisosRecogidasSolicitadas(() => push, n => Task.CompletedTask, (g, u) => { }, t => t());

            reales.Recibida(new RecogidaSolicitadaDTO { Id = 5, Empresa = "1", Cliente = CLIENTE, Contacto = "0", UsuarioSolicita = VENDEDORA, Bultos = 1 }, ALMACEN);
        }

        // ================================================================ dobles

        private class AvisosFalsos : IAvisosRecogidasSolicitadas
        {
            public readonly List<RecogidaSolicitadaDTO> Solicitadas = new List<RecogidaSolicitadaDTO>();
            public readonly List<RecogidaSolicitadaDTO> Resueltas = new List<RecogidaSolicitadaDTO>();
            public readonly List<(RecogidaSolicitadaDTO Recogida, bool AvisarAlmacen)> Anuladas = new List<(RecogidaSolicitadaDTO, bool)>();
            public readonly List<RecogidaSolicitadaDTO> SinRespuesta = new List<RecogidaSolicitadaDTO>();
            public readonly List<RecogidaSolicitadaDTO> Recibidas = new List<RecogidaSolicitadaDTO>();

            public void Recibida(RecogidaSolicitadaDTO recogida, string usuario) => Recibidas.Add(recogida);

            public void Solicitada(RecogidaSolicitadaDTO recogida, string usuario) => Solicitadas.Add(recogida);
            public void Resuelta(RecogidaSolicitadaDTO recogida, string usuario) => Resueltas.Add(recogida);
            public void Anulada(RecogidaSolicitadaDTO recogida, string usuario, bool avisarAlmacen) => Anuladas.Add((recogida, avisarAlmacen));

            Task IAvisosRecogidasSolicitadas.SinRespuesta(RecogidaSolicitadaDTO recogida)
            {
                SinRespuesta.Add(recogida);
                return Task.CompletedTask;
            }
        }

        internal class RecogidasFalsas : IRepositorioRecogidasSolicitadas
        {
            public const int PEDIDO = 930500;
            public bool Script = true;
            public readonly List<FilaRecogidaSolicitada> Filas = new List<FilaRecogidaSolicitada>();
            public readonly Dictionary<string, FichaNotaCobro> Fichas = new Dictionary<string, FichaNotaCobro>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, string> Facturas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<int, SeguimientoNotaRecogida> Seguimientos = new Dictionary<int, SeguimientoNotaRecogida>();
            public readonly List<int> NotasBorradas = new List<int>();
            public bool OtroConfirmaAntes;
            public bool NotaEmpiezaAlBorrar;
            public short BultosConfirmados;
            public string TextoConfirmado;
            public string QuienRecogeConfirmado;
            public List<string> LineasConfirmadas = new List<string>();
            public int LecturasSinAvisar;
            private int siguienteId = 1;

            public bool ScriptAplicado() => Script;

            private static FilaRecogidaSolicitada Copia(FilaRecogidaSolicitada f) => f == null ? null : (FilaRecogidaSolicitada)typeof(object)
                .GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(f, null);

            public Task<FilaRecogidaSolicitada> Leer(int id) => Task.FromResult(Copia(Filas.FirstOrDefault(f => f.Id == id)));

            public Task<List<FilaRecogidaSolicitada>> LeerDelCliente(string empresa, string cliente, string contacto)
                => Task.FromResult(Filas.Where(f => f.Cliente == cliente && (contacto == null || f.Contacto == contacto))
                    .OrderByDescending(f => f.FechaSolicitud).ThenByDescending(f => f.Id).Select(Copia).ToList());

            public Task<List<FilaRecogidaSolicitada>> LeerPendientes(string empresa)
                => Task.FromResult(Filas.Where(f => f.Estado == EstadosRecogidaSolicitada.SOLICITADA).OrderBy(f => f.FechaSolicitud).Select(Copia).ToList());

            public Task<List<FilaRecogidaSolicitada>> LeerSinAvisar()
            {
                LecturasSinAvisar++;
                return Task.FromResult(Filas.Where(f => f.Estado == EstadosRecogidaSolicitada.SOLICITADA && !f.AvisoSinRespuesta.HasValue).Select(Copia).ToList());
            }

            public Task<List<int>> LeerAbiertas(string empresa, string cliente, string contacto)
                => Task.FromResult(Filas.Where(f => f.Cliente == cliente && f.Contacto == contacto && f.Estado == EstadosRecogidaSolicitada.SOLICITADA).Select(f => f.Id).ToList());

            public Task<FichaNotaCobro> LeerFicha(string empresa, string cliente, string contacto)
                => Task.FromResult(Fichas.TryGetValue(cliente + "/" + contacto, out FichaNotaCobro ficha) ? ficha : null);

            public Task<Dictionary<string, FichaNotaCobro>> LeerFichas(string empresa, IReadOnlyCollection<string> clientes)
                => Task.FromResult(Fichas.Where(f => clientes.Contains(f.Key.Split('/')[0]))
                    .ToDictionary(f => ServicioRecogidasSolicitadas.Clave(f.Key.Split('/')[0], f.Key.Split('/')[1]), f => f.Value));

            public Task<string> LeerClienteFactura(string empresa, string factura)
                => Task.FromResult(Facturas.TryGetValue(factura, out string cliente) ? cliente : null);

            public Task<int> Insertar(FilaRecogidaSolicitada fila)
            {
                FilaRecogidaSolicitada guardada = Copia(fila);
                guardada.Id = siguienteId++;
                Filas.Add(guardada);
                return Task.FromResult(guardada.Id);
            }

            public Task<int?> Confirmar(FilaRecogidaSolicitada fila, short bultos, string texto, IReadOnlyList<string> lineas, FichaNotaCobro ficha, string usuario)
            {
                FilaRecogidaSolicitada guardada = Filas.First(f => f.Id == fila.Id);
                if (OtroConfirmaAntes || guardada.Estado != EstadosRecogidaSolicitada.SOLICITADA)
                {
                    guardada.Estado = EstadosRecogidaSolicitada.CONFIRMADA;
                    return Task.FromResult<int?>(null);
                }
                guardada.Estado = EstadosRecogidaSolicitada.CONFIRMADA;
                guardada.PedidoNota = PEDIDO;
                guardada.Bultos = bultos;
                guardada.Texto = texto;
                guardada.UsuarioResuelve = usuario;
                BultosConfirmados = bultos;
                TextoConfirmado = texto;
                QuienRecogeConfirmado = fila.QuienRecoge;
                LineasConfirmadas = lineas.ToList();
                return Task.FromResult<int?>(PEDIDO);
            }

            public Task<bool> Rechazar(int id, string motivo, string usuario)
            {
                FilaRecogidaSolicitada guardada = Filas.First(f => f.Id == id);
                if (guardada.Estado != EstadosRecogidaSolicitada.SOLICITADA)
                {
                    return Task.FromResult(false);
                }
                guardada.Estado = EstadosRecogidaSolicitada.RECHAZADA;
                guardada.MotivoRechazo = motivo;
                return Task.FromResult(true);
            }

            public Task<bool> Anular(int id, string estadoEsperado, string motivo, string usuario)
            {
                FilaRecogidaSolicitada guardada = Filas.First(f => f.Id == id);
                if (guardada.Estado != estadoEsperado)
                {
                    return Task.FromResult(false);
                }
                guardada.Estado = EstadosRecogidaSolicitada.ANULADA;
                guardada.MotivoAnulacion = motivo;
                guardada.UsuarioAnula = usuario;
                return Task.FromResult(true);
            }

            public Task<bool> AnularBorrandoNota(int id, string empresa, int pedido, string motivo, string usuario)
            {
                if (NotaEmpiezaAlBorrar)
                {
                    Seguimientos[pedido] = new SeguimientoNotaRecogida { Existe = true, Empezada = true };
                    return Task.FromResult(false);
                }
                NotasBorradas.Add(pedido);
                return Anular(id, EstadosRecogidaSolicitada.CONFIRMADA, motivo, usuario);
            }

            public Task<SeguimientoNotaRecogida> LeerSeguimiento(string empresa, int pedido)
                => Task.FromResult(Seguimientos.TryGetValue(pedido, out SeguimientoNotaRecogida s) ? s : new SeguimientoNotaRecogida { Existe = true });

            public Task<bool> MarcarAvisoSinRespuesta(int id)
            {
                FilaRecogidaSolicitada guardada = Filas.First(f => f.Id == id);
                if (guardada.AvisoSinRespuesta.HasValue || guardada.Estado != EstadosRecogidaSolicitada.SOLICITADA)
                {
                    return Task.FromResult(false);
                }
                guardada.AvisoSinRespuesta = DateTime.Now;
                return Task.FromResult(true);
            }

            public Task<bool> EsRecogidaDelVendedor(string empresa, int pedidoNota)
                => Task.FromResult(Filas.Any(f => f.PedidoNota == pedidoNota && f.QuienRecoge == QuienRecogeRecogida.VENDEDOR));

            public readonly List<int> NotasCerradas = new List<int>();
            public bool FallarAlCerrar;

            public Task<FilaRecogidaSolicitada> LeerPorNota(string empresa, int pedidoNota)
                => Task.FromResult(Copia(Filas.FirstOrDefault(f => f.PedidoNota == pedidoNota && f.Estado == EstadosRecogidaSolicitada.CONFIRMADA)));

            public Task<bool> CerrarNotaDelVendedor(string empresa, int pedidoNota, string usuario)
            {
                if (FallarAlCerrar)
                {
                    throw new InvalidOperationException("trgLinPedidoVtaUpd");
                }
                NotasCerradas.Add(pedidoNota);
                return Task.FromResult(true);
            }
        }
    }
}
