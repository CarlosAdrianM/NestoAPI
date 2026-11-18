using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infraestructure.PedidosVenta
{
    /// <summary>
    /// Caso 931007 (Carlos 18/11/26): el pedido 931007 («Todo junto») tenía todas sus líneas en el picking 100125 desde las 12:21 y a las
    /// 13:33 se le añadió el 39284 (1 ud., con stock). Se guardó sin picking y habría salido al día siguiente en otro envío, sin portes y sin
    /// que nadie lo decidiera.
    /// </summary>
    [TestClass]
    public class AmpliacionPedidoEnPreparacionTests
    {
        private static readonly DateTime Hoy = new DateTime(2026, 11, 18);

        private static LinPedidoVta Guardada(int orden, short estado = Constantes.EstadosLineaVenta.EN_CURSO, int picking = 100125,
            DateTime? albaran = null, string producto = "40010")
            => new LinPedidoVta
            {
                Empresa = "1",
                Número = 931007,
                Nº_Orden = orden,
                Producto = producto,
                Estado = estado,
                Picking = picking,
                Fecha_Albarán = albaran,
                TipoLinea = (byte)Constantes.TiposLineaVenta.PRODUCTO
            };

        /// <summary>Las 11 líneas del 931007 en el picking 100125.</summary>
        private static List<LinPedidoVta> PedidoConPicking() => Enumerable.Range(1, 11).Select(n => Guardada(1000 + n)).ToList();

        private static LineaPedidoVentaDTO Nueva(string producto = "39284", int cantidad = 1, byte tipo = Constantes.TiposLineaVenta.PRODUCTO)
            => new LineaPedidoVentaDTO { id = 0, Producto = producto, Cantidad = cantidad, tipoLinea = tipo, estado = Constantes.EstadosLineaVenta.PENDIENTE, fechaEntrega = Hoy };

        private static List<LineaPedidoVentaDTO> LineasDelPut(params LineaPedidoVentaDTO[] nuevas)
        {
            List<LineaPedidoVentaDTO> lineas = Enumerable.Range(1, 11)
                .Select(n => new LineaPedidoVentaDTO { id = 1000 + n, Producto = "40010", Cantidad = 1, tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, picking = 100125 })
                .ToList();
            lineas.AddRange(nuevas);
            return lineas;
        }

        private static EvaluacionAmpliacion Evaluar(IEnumerable<LinPedidoVta> guardadas, IEnumerable<LineaPedidoVentaDTO> put,
            IEnumerable<EnvioAmpliacionPedido> envios = null, string ruta = "FW", string motivoNoSale = null, bool nota = false)
            => AmpliacionPedidoEnPreparacion.Evaluar(931007, guardadas, nota, put, envios, ruta, motivoNoSale, Hoy);

        // Regresión del caso: a las 13:33 (pasado el corte) añadir una línea a un pedido con picking ya no pasa en silencio
        [TestMethod]
        public void Evaluar_Caso931007_LineaAnadidaConElPickingSacado_EstaEnPreparacionYOfreceLasDosOpciones()
        {
            EvaluacionAmpliacion e = Evaluar(PedidoConPicking(), LineasDelPut(Nueva()));

            Assert.AreEqual(DecisionAmpliacion.EnPreparacion, e.Decision);
            CollectionAssert.AreEqual(new[] { 100125 }, e.Pickings);
            Assert.IsFalse(e.AgenciaYaLoTiene);
            CollectionAssert.AreEqual(new[] { OpcionesAmpliacionEnPreparacion.PEDIDO_NUEVO, OpcionesAmpliacionEnPreparacion.PEDIR_ALMACEN }, e.Opciones);
            Assert.AreEqual("El pedido 931007 ya está en preparación (picking 100125) y sale hoy. Haga un pedido nuevo con lo que quiera añadir o, " +
                "desde Nesto, pida a Almacén que lo meta en el mismo envío.", e.Mensaje);
            Assert.AreEqual(1, e.LineasAMover.Count);
            Assert.AreEqual("39284", e.LineasAMover[0].Producto);
        }

        [TestMethod]
        public void Evaluar_SinLineasNuevasDeProducto_Nada()
        {
            Assert.AreEqual(DecisionAmpliacion.Nada, Evaluar(PedidoConPicking(), LineasDelPut()).Decision);
            Assert.AreEqual(DecisionAmpliacion.Nada, Evaluar(PedidoConPicking(), LineasDelPut(Nueva("Regalo", 1, Constantes.TiposLineaVenta.TEXTO))).Decision,
                "Una línea de texto sola no cambia nada");
            Assert.AreEqual(DecisionAmpliacion.Nada, Evaluar(PedidoConPicking(), LineasDelPut(Nueva("62400003", 1, Constantes.TiposLineaVenta.CUENTA_CONTABLE))).Decision,
                "Una cuenta contable sola (portes, ajuste) no cambia nada");
            Assert.AreEqual(DecisionAmpliacion.Nada, Evaluar(PedidoConPicking(), LineasDelPut(Nueva("39284", -1))).Decision, "Un abono no es ampliar");
        }

        [TestMethod]
        public void Evaluar_ChequeRegaloCuponOCurso_NoCuentan()
        {
            LineaPedidoVentaDTO cheque = Nueva();
            cheque.EsChequeRegalo = true;
            LineaPedidoVentaDTO cupon = Nueva();
            cupon.CodigoCupon = "FERIA";
            LineaPedidoVentaDTO curso = Nueva();
            curso.EventoId = 12;

            Assert.AreEqual(DecisionAmpliacion.Nada, Evaluar(PedidoConPicking(), LineasDelPut(cheque, cupon, curso)).Decision);
        }

        [TestMethod]
        public void Evaluar_SinPickingVivo_PendientePorFaltaDeStock_Nada()
        {
            List<LinPedidoVta> sinPicking = Enumerable.Range(1, 3).Select(n => Guardada(n, Constantes.EstadosLineaVenta.PENDIENTE, 0)).ToList();

            Assert.AreEqual(DecisionAmpliacion.Nada, Evaluar(sinPicking, LineasDelPut(Nueva())).Decision);
        }

        [TestMethod]
        public void Evaluar_AlbaranDeHoySinPickingVivo_EnPreparacion_AlbaranDeAyerNo()
        {
            var deHoy = new List<LinPedidoVta> { Guardada(1, Constantes.EstadosLineaVenta.ALBARAN, 0, Hoy.AddHours(12)) };
            var deAyer = new List<LinPedidoVta> { Guardada(1, Constantes.EstadosLineaVenta.ALBARAN, 0, Hoy.AddDays(-1)) };

            EvaluacionAmpliacion hoy = Evaluar(deHoy, LineasDelPut(Nueva()));
            Assert.AreEqual(DecisionAmpliacion.EnPreparacion, hoy.Decision);
            StringAssert.StartsWith(hoy.Mensaje, "El pedido 931007 ya está en preparación (albarán de hoy)");
            Assert.AreEqual(DecisionAmpliacion.Nada, Evaluar(deAyer, LineasDelPut(Nueva())).Decision);
        }

        [TestMethod]
        public void Evaluar_PresupuestoONotaDeEntrega_Nada()
        {
            var presupuesto = new List<LinPedidoVta> { Guardada(1, Constantes.EstadosLineaVenta.PRESUPUESTO, 0), Guardada(2, Constantes.EstadosLineaVenta.ALBARAN, 0, Hoy) };

            Assert.AreEqual(DecisionAmpliacion.Nada, Evaluar(presupuesto, LineasDelPut(Nueva())).Decision);
            Assert.AreEqual(DecisionAmpliacion.Nada, Evaluar(PedidoConPicking(), LineasDelPut(Nueva()), nota: true).Decision);
        }

        [TestMethod]
        public void Evaluar_RetenidoOAplazado_NoSaleHoy_SeGuardaYSeAvisa()
        {
            EvaluacionAmpliacion e = Evaluar(PedidoConPicking(), LineasDelPut(Nueva()), motivoNoSale: "El pedido está aplazado al 20/11/2026.");

            Assert.AreEqual(DecisionAmpliacion.NoSaleHoy, e.Decision);
            Assert.AreEqual("El pedido está aplazado al 20/11/2026.", e.Mensaje);
            CollectionAssert.AreEqual(new[] { 100125 }, e.Pickings);
        }

        [TestMethod]
        public void Evaluar_LaAgenciaYaTieneElEnvioDeEsePicking_SoloPedidoNuevo()
        {
            var envio = new EnvioAmpliacionPedido { Numero = 251300, Estado = Constantes.Agencias.ESTADO_TRAMITADO, Agencia = 1, Pickings = new List<int> { 100125 } };

            EvaluacionAmpliacion e = Evaluar(PedidoConPicking(), LineasDelPut(Nueva()), new[] { envio });

            Assert.IsTrue(e.AgenciaYaLoTiene);
            Assert.AreEqual(251300, e.Envio);
            CollectionAssert.AreEqual(new[] { OpcionesAmpliacionEnPreparacion.PEDIDO_NUEVO }, e.Opciones);
            Assert.AreEqual("El pedido 931007 ya está en preparación (picking 100125) y la agencia ya tiene su envío 251300: lo que quiera añadir no " +
                "puede ir en él. Haga un pedido nuevo con lo que quiera añadir.", e.Mensaje);
        }

        [TestMethod]
        public void Evaluar_EnvioTramitadoDeOtroPicking_LaAgenciaNoLoTiene()
        {
            var envio = new EnvioAmpliacionPedido { Numero = 250999, Estado = Constantes.Agencias.ESTADO_TRAMITADO, Agencia = 1, Pickings = new List<int> { 100095 } };

            EvaluacionAmpliacion e = Evaluar(PedidoConPicking(), LineasDelPut(Nueva()), new[] { envio });

            Assert.IsFalse(e.AgenciaYaLoTiene);
            Assert.IsTrue(e.PuedePedirAlmacen);
        }

        [TestMethod]
        public void Evaluar_RepartoPropio_SoloLoTieneSiEstaCargadoEnLaFurgoneta()
        {
            var sinCargar = new EnvioAmpliacionPedido { Numero = 251400, Estado = Constantes.Agencias.ESTADO_TRAMITADO, Agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO };
            var cargado = new EnvioAmpliacionPedido
            {
                Numero = 251401, Estado = Constantes.Agencias.ESTADO_TRAMITADO, Agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO, CargadoEnFurgoneta = true
            };

            Assert.IsTrue(Evaluar(PedidoConPicking(), LineasDelPut(Nueva()), new[] { sinCargar }).PuedePedirAlmacen);
            EvaluacionAmpliacion e = Evaluar(PedidoConPicking(), LineasDelPut(Nueva()), new[] { cargado });
            Assert.IsTrue(e.AgenciaYaLoTiene);
            Assert.IsFalse(e.PuedePedirAlmacen);
            StringAssert.Contains(e.Mensaje, "su envío 251401 ya está cargado en la furgoneta");
        }

        [TestMethod]
        public void Evaluar_EtiquetaEnCursoConReembolso_SoloPedidoNuevo()
        {
            var envio = new EnvioAmpliacionPedido { Numero = 251500, Estado = Constantes.Agencias.ESTADO_EN_CURSO, Agencia = 1, Reembolso = 120m };

            EvaluacionAmpliacion e = Evaluar(PedidoConPicking(), LineasDelPut(Nueva()), new[] { envio });

            Assert.IsFalse(e.AgenciaYaLoTiene);
            Assert.IsFalse(e.PuedePedirAlmacen);
            Assert.AreEqual(251500, e.Envio);
            StringAssert.Contains(e.Mensaje, "la etiqueta de su envío 251500 ya está hecha con reembolso");
        }

        [TestMethod]
        public void Evaluar_EtiquetaEnCursoSinReembolso_SePuedePedirAAlmacen()
        {
            var envio = new EnvioAmpliacionPedido { Numero = 251501, Estado = Constantes.Agencias.ESTADO_EN_CURSO, Agencia = 1 };

            EvaluacionAmpliacion e = Evaluar(PedidoConPicking(), LineasDelPut(Nueva()), new[] { envio });

            Assert.IsTrue(e.PuedePedirAlmacen);
            Assert.AreEqual(251501, e.Envio);
        }

        [TestMethod]
        public void Evaluar_RecogidaEnTienda_LasDosOpcionesYSuTexto()
        {
            EvaluacionAmpliacion e = Evaluar(PedidoConPicking(), LineasDelPut(Nueva()), ruta: "AM");

            Assert.IsTrue(e.RecogeEnTienda);
            Assert.IsTrue(e.PuedePedirAlmacen);
            Assert.AreEqual("El pedido 931007 ya está en preparación (picking 100125) para recogerlo en la tienda. Haga un pedido nuevo con lo que " +
                "quiera añadir o, desde Nesto, pida a Almacén que lo prepare con lo demás.", e.Mensaje);
        }

        [TestMethod]
        public void LineasAMover_LasDeTextoNuevasVanConLosProductos_LasCuentasNo()
        {
            LineaPedidoVentaDTO producto = Nueva();
            LineaPedidoVentaDTO texto = Nueva("Para la cabina 2", 1, Constantes.TiposLineaVenta.TEXTO);
            LineaPedidoVentaDTO portes = Nueva("62400003", 1, Constantes.TiposLineaVenta.CUENTA_CONTABLE);

            List<LineaPedidoVentaDTO> mover = AmpliacionPedidoEnPreparacion.LineasAMover(LineasDelPut(producto, texto, portes));

            CollectionAssert.AreEqual(new[] { producto, texto }, mover);
        }

        [TestMethod]
        public void TextoPickings_UnoVariosONinguno()
        {
            Assert.AreEqual("picking 100125", AmpliacionPedidoEnPreparacion.TextoPickings(new[] { 100125 }));
            Assert.AreEqual("pickings 100125, 100126 y 100130", AmpliacionPedidoEnPreparacion.TextoPickings(new[] { 100130, 100125, 100126 }));
            Assert.AreEqual("albarán de hoy", AmpliacionPedidoEnPreparacion.TextoPickings(new int[0]));
        }

        [TestMethod]
        public void ConstruirPedidoNuevo_CabeceraDelOriginalSinPrepagosNiModosYLasLineasComoNuevas()
        {
            var original = new PedidoVentaDTO
            {
                empresa = "1", numero = 931007, cliente = "15191", contacto = "0", formaPago = "EFC", plazosPago = "PRE", iva = "G21",
                vendedor = "NV", ruta = "FW", serie = "NV", suPedido = "PO-77", comentarios = "Dejar en portería", modoServicio = 1, servirJunto = true,
                Usuario = "NUEVAVISION\\Laura"
            };
            original.Prepagos.Add(new PrepagoDTO { Importe = 100 });
            LineaPedidoVentaDTO linea = Nueva();
            linea.fechaEntrega = Hoy.AddDays(-2);
            linea.picking = 5;

            PedidoVentaDTO nuevo = AmpliacionPedidoEnPreparacion.ConstruirPedidoNuevo(original, new[] { linea }, "NUEVAVISION\\Laura", Hoy, false);

            Assert.AreEqual(0, nuevo.numero);
            Assert.AreEqual("15191", nuevo.cliente);
            Assert.AreEqual("PRE", nuevo.plazosPago);
            Assert.AreEqual("PO-77", nuevo.suPedido);
            Assert.AreEqual(Hoy, nuevo.fecha);
            Assert.IsNull(nuevo.modoServicio, "Nace en el modo por defecto");
            Assert.IsFalse(nuevo.servirJunto);
            Assert.AreEqual(0, nuevo.Prepagos.Count, "El pedido nuevo no hereda el prepago");
            Assert.AreEqual("Añadido al pedido 931007 cuando ya estaba en preparación.", nuevo.comentarios);
            Assert.AreEqual(1, nuevo.Lineas.Count);
            Assert.AreEqual(0, linea.id);
            Assert.AreEqual(0, linea.picking);
            Assert.AreEqual(Hoy, linea.fechaEntrega, "Una fecha de entrega pasada se queda en hoy");
            Assert.AreSame(nuevo, linea.Pedido);
        }

        [TestMethod]
        public void AvisoPedidoNuevo_ConPrepagoLoDice()
        {
            var original = new PedidoVentaDTO { numero = 931007, plazosPago = "PRE" };

            Assert.AreEqual("El pedido 931007 ya estaba en preparación: 39284 (1 ud.) va en el pedido nuevo 931050, con sus propios portes. " +
                "El pedido 931007 es de prepago: el pedido 931050 no lleva su prepago, cóbrelo aparte.",
                AmpliacionPedidoEnPreparacion.AvisoPedidoNuevo(original, 931050, new[] { Nueva() }));
            original.plazosPago = "CONTADO";
            Assert.AreEqual("El pedido 931007 ya estaba en preparación: 39284 (1 ud.) va en el pedido nuevo 931050, con sus propios portes.",
                AmpliacionPedidoEnPreparacion.AvisoPedidoNuevo(original, 931050, new[] { Nueva() }));
        }

        [TestMethod]
        public void Excepcion_Es409ConCodigoYDetalles()
        {
            EvaluacionAmpliacion e = Evaluar(PedidoConPicking(), LineasDelPut(Nueva()));

            PedidoEnPreparacionException ex = AmpliacionPedidoEnPreparacion.Excepcion("1  ", 931007, e);

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            Assert.AreEqual("PEDIDO_EN_PREPARACION", ex.GetErrorCode());
            Assert.AreEqual(e.Mensaje, ex.Message);
            Assert.AreEqual("1", ex.Context.Empresa);
            CollectionAssert.AreEqual(new[] { 100125 }, (List<int>)ex.Context.AdditionalData["Pickings"]);
            Assert.AreEqual(false, ex.Context.AdditionalData["AgenciaYaLoTiene"]);
            CollectionAssert.AreEqual(e.Opciones, (List<string>)ex.Context.AdditionalData["Opciones"]);
            CollectionAssert.AreEqual(new[] { "39284" }, (List<string>)ex.Context.AdditionalData["Productos"]);
            Assert.IsTrue(ex.Context.AdditionalData.ContainsKey("Envio"));
        }

        [TestMethod]
        public void Opciones_Normalizar()
        {
            Assert.AreEqual(OpcionesAmpliacionEnPreparacion.PEDIDO_NUEVO, OpcionesAmpliacionEnPreparacion.Normalizar(" pedidonuevo ", out bool v1));
            Assert.IsTrue(v1);
            Assert.IsNull(OpcionesAmpliacionEnPreparacion.Normalizar(null, out bool v2));
            Assert.IsTrue(v2);
            Assert.IsNull(OpcionesAmpliacionEnPreparacion.Normalizar("SaleAparte", out bool v3));
            Assert.IsFalse(v3, "«Añadirlo sabiendo que sale aparte» no existe");
        }

        [TestMethod]
        public void NotificacionesUrgentes_SoloLaPreguntaAAlmacenEsUrgente()
        {
            Assert.IsTrue(NotificacionesUrgentes.EsUrgente(TextosAmpliacionPedido.TIPO_NOTIFICACION, TextosAmpliacionPedido.ACCION_PREGUNTA));
            Assert.IsFalse(NotificacionesUrgentes.EsUrgente(TextosAmpliacionPedido.TIPO_NOTIFICACION, TextosAmpliacionPedido.ACCION_RESPUESTA));
            Assert.IsFalse(NotificacionesUrgentes.EsUrgente(TextosAmpliacionPedido.TIPO_NOTIFICACION, TextosAmpliacionPedido.ACCION_AVISO));
        }

        [TestMethod]
        public void Pregunta_LlevaLaReferenciaYLaPregunta()
        {
            var s = new AmpliacionPedidoDTO { Id = 12, Empresa = "1", Pedido = 931007, Productos = "39284 (1 ud.)", Pickings = "100125", Usuario = "NUEVAVISION\\Laura" };

            NotificacionPushDTO n = TextosAmpliacionPedido.Pregunta(s, "15191", "Peluquería Lucía");

            Assert.AreEqual("Pedido 931007: ¿entra en el envío de hoy?", n.Titulo);
            Assert.AreEqual("12", n.Datos[NotificacionesUrgentes.DATO_REFERENCIA]);
            Assert.AreEqual("pregunta", n.Datos[NotificacionesUrgentes.DATO_ACCION]);
            Assert.IsTrue(NotificacionesUrgentes.EsUrgente(n));
            StringAssert.StartsWith(n.Cuerpo, "Laura ha añadido 39284 (1 ud.) al pedido 931007 del cliente 15191 (Peluquería Lucía), que ya está en preparación (picking 100125).");
            Assert.IsFalse(n.Datos.Values.Any(v => v == null), "Las push no admiten valores nulos");
        }

        [TestMethod]
        public void Respuesta_SinRespuestaPedidoNuevo_YEntra()
        {
            var s = new AmpliacionPedidoDTO { Id = 12, Empresa = "1", Pedido = 931007, Productos = "39284 (1 ud.)", UsuarioRespuesta = "NUEVAVISION\\Alfredo" };

            Assert.AreEqual("Almacén no ha contestado en 60 minutos: 39284 (1 ud.) va en el pedido nuevo 931050, con sus propios portes.",
                TextosAmpliacionPedido.Respuesta(s, EstadosAmpliacionPedido.SIN_RESPUESTA, null, 931050, null).Cuerpo);
            Assert.AreEqual("Almacén (Alfredo) mete 39284 (1 ud.) en el envío de hoy del pedido 931007 (picking 100140).",
                TextosAmpliacionPedido.Respuesta(s, EstadosAmpliacionPedido.ENTRA, 100140, null, null).Cuerpo);
            Assert.AreEqual("Almacén (Alfredo) dice que no llega al envío de hoy: 39284 (1 ud.) va en el pedido nuevo 931050, con sus propios portes. " +
                "Motivo: Ya se ha ido la agencia.", TextosAmpliacionPedido.Respuesta(s, EstadosAmpliacionPedido.NO_ENTRA, null, 931050, "Ya se ha ido la agencia").Cuerpo);
        }

        [TestMethod]
        public void Resuelta_ResultadoPorEstado()
        {
            var s = new AmpliacionPedidoDTO { Id = 12, UsuarioRespuesta = "NUEVAVISION\\Alfredo" };

            Assert.AreEqual("Entra", TextosAmpliacionPedido.Resuelta(s, EstadosAmpliacionPedido.ENTRA).Resultado);
            Assert.AreEqual("NoEntra", TextosAmpliacionPedido.Resuelta(s, EstadosAmpliacionPedido.NO_ENTRA).Resultado);
            NotificacionResueltaDTO sinRespuesta = TextosAmpliacionPedido.Resuelta(s, EstadosAmpliacionPedido.SIN_RESPUESTA);
            Assert.AreEqual("SinRespuesta", sinRespuesta.Resultado);
            Assert.AreEqual("12", sinRespuesta.Referencia);
            Assert.AreEqual("Alfredo", TextosAmpliacionPedido.Resuelta(s, EstadosAmpliacionPedido.ENTRA).Respondio);
        }

        [TestMethod]
        public void MotivoNoSePuedenMover()
        {
            Assert.AreEqual("lo añadido ya no está en el pedido", PasoAmpliacionAPedidoNuevo.MotivoNoSePuedenMover(new List<LineaPedidoVentaDTO>(), new[] { 5 }));
            var conPicking = new LineaPedidoVentaDTO { id = 5, picking = 100140, estado = Constantes.EstadosLineaVenta.EN_CURSO };
            Assert.AreEqual("lo añadido ya tiene picking o albarán: no se ha tocado", PasoAmpliacionAPedidoNuevo.MotivoNoSePuedenMover(new[] { conPicking }, new[] { 5 }));
            var libre = new LineaPedidoVentaDTO { id = 5, picking = 0, estado = Constantes.EstadosLineaVenta.PENDIENTE };
            Assert.IsNull(PasoAmpliacionAPedidoNuevo.MotivoNoSePuedenMover(new[] { libre }, new[] { 5 }));
        }

        [TestMethod]
        public void AmpliacionPedidoDTO_LeeYEscribeLosNumeros()
        {
            Assert.AreEqual("5,7", AmpliacionPedidoDTO.Enteros(new[] { 5, 0, 7, 5 }));
            CollectionAssert.AreEqual(new[] { 5, 7 }, new AmpliacionPedidoDTO { Lineas = " 5, 7,x" }.NumerosOrden());
        }

        [TestMethod]
        public void CambioModoConPicking_SigueIgualConLasReglasCompartidas()
        {
            Assert.IsNotNull(CambioModoConPicking.Motivo(PedidoConPicking(), Hoy));
            Assert.IsNotNull(CambioModoConPicking.Motivo(new[] { Guardada(1, Constantes.EstadosLineaVenta.ALBARAN, 0, Hoy) }, Hoy));
            Assert.IsNull(CambioModoConPicking.Motivo(new[] { Guardada(1, Constantes.EstadosLineaVenta.PENDIENTE, 0) }, Hoy));
        }
    }

    /// <summary>Caso 931007: el servicio (con el lector, el repositorio y los avisos falsos).</summary>
    [TestClass]
    public class ServicioAmpliacionPedidoEnPreparacionTests
    {
        private static readonly DateTime Ahora = new DateTime(2026, 11, 18, 13, 33, 0);

        private ILectorAmpliacionPedido lector;
        private IRepositorioAmpliacionesPedido repositorio;
        private IAvisosAmpliacionPedido avisos;
        private IPasoAmpliacionAPedidoNuevo paso;
        private ISacadorPickingAmpliacion sacador;
        private List<int> programados;

        [TestInitialize]
        public void Preparar()
        {
            lector = A.Fake<ILectorAmpliacionPedido>();
            repositorio = A.Fake<IRepositorioAmpliacionesPedido>();
            avisos = A.Fake<IAvisosAmpliacionPedido>();
            paso = A.Fake<IPasoAmpliacionAPedidoNuevo>();
            sacador = A.Fake<ISacadorPickingAmpliacion>();
            programados = new List<int>();
            A.CallTo(() => lector.LeerLineas("1", 931007)).Returns(Task.FromResult(Enumerable.Range(1, 11)
                .Select(n => new LinPedidoVta { Empresa = "1", Número = 931007, Nº_Orden = 1000 + n, Estado = 1, Picking = 100125 }).ToList()));
            A.CallTo(() => lector.LeerEnvios("1", 931007)).Returns(Task.FromResult(new List<EnvioAmpliacionPedido>()));
            A.CallTo(() => lector.MotivoNoSaleHoy("1", 931007)).Returns(Task.FromResult<string>(null));
            A.CallTo(() => repositorio.TablaExiste()).Returns(true);
            A.CallTo(() => avisos.Preguntar(A<NotificacionPushDTO>._)).Returns(Task.FromResult(3));
        }

        private ServicioAmpliacionPedidoEnPreparacion Servicio()
            => new ServicioAmpliacionPedidoEnPreparacion(lector, repositorio, avisos, () => Ahora, id => programados.Add(id), () => paso, () => sacador);

        private static PedidoVentaDTO Put(string opcion = null)
        {
            var pedido = new PedidoVentaDTO { empresa = "1", numero = 931007, cliente = "15191", contacto = "0", ruta = "FW", Usuario = "NUEVAVISION\\Laura", AmpliacionEnPreparacion = opcion };
            foreach (int n in Enumerable.Range(1, 11))
            {
                pedido.Lineas.Add(new LineaPedidoVentaDTO { id = 1000 + n, Producto = "40010", Cantidad = 1, tipoLinea = 1, picking = 100125 });
            }
            pedido.Lineas.Add(new LineaPedidoVentaDTO { id = 0, Producto = "39284", Cantidad = 1, tipoLinea = 1, fechaEntrega = Ahora.Date });
            return pedido;
        }

        [TestMethod]
        public async Task Comprobar_SinEleccion_409PedidoEnPreparacion()
        {
            PedidoEnPreparacionException ex = null;
            try
            {
                _ = await Servicio().Comprobar(Put());
            }
            catch (PedidoEnPreparacionException e)
            {
                ex = e;
            }

            Assert.IsNotNull(ex, "Sin elección tiene que ser un 409");
            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            StringAssert.StartsWith(ex.Message, "El pedido 931007 ya está en preparación (picking 100125) y sale hoy.");
        }

        [TestMethod]
        public async Task Comprobar_SinLineasNuevas_NoLeeNada()
        {
            PedidoVentaDTO pedido = Put();
            _ = pedido.Lineas.Remove(pedido.Lineas.Single(l => l.id == 0));

            ComprobacionAmpliacion c = await Servicio().Comprobar(pedido);

            Assert.AreEqual(AccionAmpliacion.Seguir, c.Accion);
            A.CallTo(() => lector.LeerLineas(A<string>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Comprobar_YaResuelta_Sigue()
        {
            PedidoVentaDTO pedido = Put();
            pedido.AmpliacionResuelta = true;

            Assert.AreEqual(AccionAmpliacion.Seguir, (await Servicio().Comprobar(pedido)).Accion);
        }

        [TestMethod]
        public async Task Comprobar_PedidoNuevo_YPedirAlmacen()
        {
            Assert.AreEqual(AccionAmpliacion.PedidoNuevo, (await Servicio().Comprobar(Put("PedidoNuevo"))).Accion);
            Assert.AreEqual(AccionAmpliacion.PedirAlmacen, (await Servicio().Comprobar(Put("PedirAlmacen"))).Accion);
        }

        [TestMethod]
        public async Task Comprobar_PedirAlmacenConLaAgenciaQueYaLoTiene_409()
        {
            A.CallTo(() => lector.LeerEnvios("1", 931007)).Returns(Task.FromResult(new List<EnvioAmpliacionPedido>
            {
                new EnvioAmpliacionPedido { Numero = 251300, Estado = Constantes.Agencias.ESTADO_TRAMITADO, Agencia = 1 }
            }));

            await Assert.ThrowsExceptionAsync<PedidoEnPreparacionException>(() => Servicio().Comprobar(Put("PedirAlmacen")));
        }

        [TestMethod]
        public async Task Comprobar_PedirAlmacenSinTabla_503()
        {
            A.CallTo(() => repositorio.TablaExiste()).Returns(false);

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Comprobar(Put("PedirAlmacen")));

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            Assert.AreEqual(AccionAmpliacion.PedidoNuevo, (await Servicio().Comprobar(Put("PedidoNuevo"))).Accion, "Sin la tabla, PedidoNuevo sigue funcionando");
        }

        [TestMethod]
        public async Task Comprobar_OpcionQueNoExiste_400()
        {
            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Comprobar(Put("SaleAparte")));

            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [TestMethod]
        public async Task Comprobar_PedidoQueHoyNoSale_SeGuardaYSeAvisaAAlmacen()
        {
            A.CallTo(() => lector.MotivoNoSaleHoy("1", 931007)).Returns(Task.FromResult("Pedido retenido por Alfredo"));

            ComprobacionAmpliacion c = await Servicio().Comprobar(Put());

            Assert.AreEqual(AccionAmpliacion.AvisarAlmacen, c.Accion);
            _ = await Servicio().DespuesDeGuardar(Put(), c, null, "Peluquería Lucía");
            A.CallTo(() => avisos.AvisarAlmacen(A<NotificacionPushDTO>.That.Matches(n => n.Cuerpo.Contains("hoy no sale (Pedido retenido por Alfredo)"))))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task DespuesDeGuardar_PedirAlmacen_ApuntaLaSolicitudProgramaElVencimientoYPreguntaUrgente()
        {
            PedidoVentaDTO pedido = Put("PedirAlmacen");
            ComprobacionAmpliacion c = await Servicio().Comprobar(pedido);
            LineaPedidoVentaDTO nueva = pedido.Lineas.Single(l => l.id == 0);
            A.CallTo(() => repositorio.Insertar(A<AmpliacionPedidoDTO>._)).Returns(Task.FromResult(12));

            AvisoPedidoDTO aviso = await Servicio().DespuesDeGuardar(pedido, c,
                new[] { new KeyValuePair<LineaPedidoVentaDTO, LinPedidoVta>(nueva, new LinPedidoVta { Nº_Orden = 4321 }) }, "Peluquería Lucía");

            A.CallTo(() => repositorio.Insertar(A<AmpliacionPedidoDTO>.That.Matches(s =>
                s.Lineas == "4321" && s.Pickings == "100125" && s.Productos == "39284 (1 ud.)" && s.Usuario == "NUEVAVISION\\Laura"))).MustHaveHappenedOnceExactly();
            CollectionAssert.AreEqual(new[] { 12 }, programados);
            A.CallTo(() => avisos.Preguntar(A<NotificacionPushDTO>.That.Matches(n => n.Datos["referencia"] == "12"))).MustHaveHappenedOnceExactly();
            Assert.AreEqual(AmpliacionPedidoEnPreparacion.TIPO_AVISO_PEDIDO_A_ALMACEN, aviso.Tipo);
            Assert.AreEqual("Se ha pedido a Almacén que meta 39284 (1 ud.) en el envío de hoy del pedido 931007. Le avisaremos con su respuesta; " +
                "si no contestan en 60 minutos, irá en un pedido nuevo con sus propios portes.", aviso.Mensaje);
            Assert.IsTrue(c.ProductosPendientesDeAlmacen.Contains("39284"));
        }

        [TestMethod]
        public async Task Separar_GuardaElOriginalSinLoAnadidoYCreaElNuevoEnLaMismaOperacion()
        {
            PedidoVentaDTO pedido = Put("PedidoNuevo");
            ComprobacionAmpliacion c = await Servicio().Comprobar(pedido);
            PedidoVentaDTO guardado = null;
            PedidoVentaDTO creado = null;
            var controlador = new ControladorFalso();

            ResultadoSeparacion r = await ServicioAmpliacionPedidoEnPreparacion.SepararDelPut(pedido, c,
                p => { guardado = p; return Task.FromResult<IHttpActionResult>(new OkNegotiatedContentResult<RespuestaModificacionPedidoDTO>(new RespuestaModificacionPedidoDTO { Version = "v2" }, controlador)); },
                p => { creado = p; p.numero = 931050; return Task.FromResult<IHttpActionResult>(new CreatedAtRouteNegotiatedContentResult<PedidoVentaDTO>("DefaultApi", new Dictionary<string, object>(), p, controlador)); },
                Ahora.Date);

            Assert.IsNull(r.Error);
            Assert.AreEqual(931050, r.PedidoNuevo);
            Assert.AreEqual(931050, r.Respuesta.PedidoNuevo);
            Assert.AreEqual("v2", r.Respuesta.Version, "La versión es la del original guardado");
            Assert.AreEqual(11, guardado.Lineas.Count, "El original se guarda sin lo añadido");
            Assert.IsTrue(guardado.AmpliacionResuelta);
            Assert.AreEqual(1, creado.Lineas.Count);
            Assert.AreEqual("39284", creado.Lineas.Single().Producto);
            Assert.IsTrue(r.Respuesta.Avisos.Any(a => a.Tipo == AmpliacionPedidoEnPreparacion.TIPO_AVISO_PEDIDO_NUEVO));
            Assert.AreEqual(931050, pedido.PedidoNuevoAmpliacion, "UnirPedidos devuelve el DTO del original: lleva el número");
        }

        [TestMethod]
        public async Task Separar_SiElOriginalNoSeGuarda_NoSeCreaElNuevo()
        {
            PedidoVentaDTO pedido = Put("PedidoNuevo");
            ComprobacionAmpliacion c = await Servicio().Comprobar(pedido);
            bool creado = false;
            var controlador = new ControladorFalso();
            var rechazo = new BadRequestErrorMessageResult("No se puede modificar un pedido ya facturado", controlador);

            ResultadoSeparacion r = await ServicioAmpliacionPedidoEnPreparacion.SepararDelPut(pedido, c,
                p => Task.FromResult<IHttpActionResult>(rechazo),
                p => { creado = true; return Task.FromResult<IHttpActionResult>(null); }, Ahora.Date);

            Assert.AreSame(rechazo, r.Error);
            Assert.IsFalse(creado);
            Assert.IsNull(r.PedidoNuevo);
        }

        [TestMethod]
        public async Task Separar_SiElNuevoNoSeCrea_DevuelveSuRechazo()
        {
            PedidoVentaDTO pedido = Put("PedidoNuevo");
            ComprobacionAmpliacion c = await Servicio().Comprobar(pedido);
            var controlador = new ControladorFalso();
            var rechazo = new BadRequestErrorMessageResult("El pedido tiene que tener formas y plazos de pago", controlador);

            ResultadoSeparacion r = await ServicioAmpliacionPedidoEnPreparacion.SepararDelPut(pedido, c,
                p => Task.FromResult<IHttpActionResult>(new OkResult(controlador)),
                p => Task.FromResult<IHttpActionResult>(rechazo), Ahora.Date);

            Assert.AreSame(rechazo, r.Error);
            StringAssert.Contains(r.MensajeError, "El pedido tiene que tener formas y plazos de pago");
        }

        private static AmpliacionPedidoDTO Pendiente(DateTime? fecha = null) => new AmpliacionPedidoDTO
        {
            Id = 12, Empresa = "1", Pedido = 931007, Lineas = "4321", Productos = "39284 (1 ud.)", Pickings = "100125",
            Usuario = "NUEVAVISION\\Laura", Fecha = fecha ?? Ahora, Estado = EstadosAmpliacionPedido.PENDIENTE
        };

        [TestMethod]
        public async Task Responder_Si_SacaElPickingYAvisa()
        {
            A.CallTo(() => repositorio.Leer(12)).Returns(Task.FromResult(Pendiente()));
            A.CallTo(() => repositorio.Reclamar(12, EstadosAmpliacionPedido.ENTRA, "NUEVAVISION\\Alfredo", Ahora)).Returns(Task.FromResult(true));
            A.CallTo(() => sacador.Sacar("1", 931007, A<IReadOnlyCollection<int>>._)).Returns(Task.FromResult<(int?, string)>((100140, null)));

            ResultadoRespuestaAmpliacion r = await Servicio().Responder(12, true, null, "NUEVAVISION\\Alfredo");

            A.CallTo(() => repositorio.Cerrar(12, EstadosAmpliacionPedido.ENTRA, 100140, null, null)).MustHaveHappenedOnceExactly();
            A.CallTo(() => avisos.Resuelta(A<NotificacionResueltaDTO>.That.Matches(x => x.Resultado == "Entra" && x.Referencia == "12"))).MustHaveHappenedOnceExactly();
            A.CallTo(() => avisos.AvisarA("NUEVAVISION\\Laura", A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => paso.Pasar(A<AmpliacionPedidoDTO>._)).MustNotHaveHappened();
            StringAssert.Contains(r.Mensaje, "picking 100140");
        }

        [TestMethod]
        public async Task Responder_SiPeroSinPicking_CuentaComoNoConSuMotivo()
        {
            A.CallTo(() => repositorio.Leer(12)).Returns(Task.FromResult(Pendiente()));
            A.CallTo(() => repositorio.Reclamar(12, EstadosAmpliacionPedido.ENTRA, A<string>._, A<DateTime>._)).Returns(Task.FromResult(true));
            A.CallTo(() => sacador.Sacar("1", 931007, A<IReadOnlyCollection<int>>._)).Returns(Task.FromResult<(int?, string)>((null, "No hay stock para asignar picking")));
            A.CallTo(() => paso.Pasar(A<AmpliacionPedidoDTO>._)).Returns(Task.FromResult(new ResultadoSeparacion { PedidoNuevo = 931050 }));

            _ = await Servicio().Responder(12, true, null, "NUEVAVISION\\Alfredo");

            A.CallTo(() => repositorio.Cerrar(12, EstadosAmpliacionPedido.NO_ENTRA, null, 931050,
                "no se ha podido sacar el picking de lo añadido (No hay stock para asignar picking)")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Responder_No_PasaAPedidoNuevo()
        {
            A.CallTo(() => repositorio.Leer(12)).Returns(Task.FromResult(Pendiente()));
            A.CallTo(() => repositorio.Reclamar(12, EstadosAmpliacionPedido.NO_ENTRA, A<string>._, A<DateTime>._)).Returns(Task.FromResult(true));
            A.CallTo(() => paso.Pasar(A<AmpliacionPedidoDTO>._)).Returns(Task.FromResult(new ResultadoSeparacion { PedidoNuevo = 931050 }));

            ResultadoRespuestaAmpliacion r = await Servicio().Responder(12, false, "Ya se ha ido la agencia", "NUEVAVISION\\Alfredo");

            A.CallTo(() => sacador.Sacar(A<string>._, A<int>._, A<IReadOnlyCollection<int>>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.Cerrar(12, EstadosAmpliacionPedido.NO_ENTRA, null, 931050, "Ya se ha ido la agencia")).MustHaveHappenedOnceExactly();
            A.CallTo(() => avisos.AvisarA("NUEVAVISION\\Laura", A<NotificacionPushDTO>.That.Matches(n => n.Datos["pedidoNuevo"] == "931050"))).MustHaveHappenedOnceExactly();
            StringAssert.Contains(r.Mensaje, "pedido nuevo 931050");
        }

        [TestMethod]
        public async Task Responder_SiNoSePuedeMover_AvisaAAlmacenYAQuienLoPidio()
        {
            A.CallTo(() => repositorio.Leer(12)).Returns(Task.FromResult(Pendiente()));
            A.CallTo(() => repositorio.Reclamar(12, EstadosAmpliacionPedido.NO_ENTRA, A<string>._, A<DateTime>._)).Returns(Task.FromResult(true));
            A.CallTo(() => paso.Pasar(A<AmpliacionPedidoDTO>._)).Returns(Task.FromResult(new ResultadoSeparacion { MensajeError = "lo añadido ya tiene picking o albarán: no se ha tocado" }));

            ResultadoRespuestaAmpliacion r = await Servicio().Responder(12, false, null, "NUEVAVISION\\Alfredo");

            A.CallTo(() => avisos.AvisarAlmacen(A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            StringAssert.Contains(r.Mensaje, "No se ha podido pasar lo añadido a un pedido nuevo");
        }

        [TestMethod]
        public async Task Responder_YaContestada_409()
        {
            AmpliacionPedidoDTO contestada = Pendiente();
            contestada.Estado = EstadosAmpliacionPedido.ENTRA;
            contestada.UsuarioRespuesta = "NUEVAVISION\\Pedro";
            A.CallTo(() => repositorio.Leer(12)).Returns(Task.FromResult(contestada));

            ResultadoRespuestaAmpliacion r = await Servicio().Responder(12, false, null, "NUEVAVISION\\Alfredo");

            Assert.AreEqual("Ya la ha contestado Pedro: lo añadido al pedido 931007 entra en el envío de hoy.", r.Conflicto);
            A.CallTo(() => repositorio.Reclamar(A<int>._, A<string>._, A<string>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Vencer_AntesDeLaHora_NoHaceNada_DespuesPasaAPedidoNuevo()
        {
            A.CallTo(() => repositorio.Leer(12)).Returns(Task.FromResult(Pendiente(Ahora.AddMinutes(-30))));
            Assert.IsNull(await Servicio().Vencer(12));
            A.CallTo(() => repositorio.Reclamar(A<int>._, A<string>._, A<string>._, A<DateTime>._)).MustNotHaveHappened();

            A.CallTo(() => repositorio.Leer(12)).Returns(Task.FromResult(Pendiente(Ahora.AddMinutes(-60))));
            A.CallTo(() => repositorio.Reclamar(12, EstadosAmpliacionPedido.SIN_RESPUESTA, A<string>._, Ahora)).Returns(Task.FromResult(true));
            A.CallTo(() => paso.Pasar(A<AmpliacionPedidoDTO>._)).Returns(Task.FromResult(new ResultadoSeparacion { PedidoNuevo = 931050 }));

            _ = await Servicio().Vencer(12);

            A.CallTo(() => repositorio.Cerrar(12, EstadosAmpliacionPedido.SIN_RESPUESTA, null, 931050, null)).MustHaveHappenedOnceExactly();
            A.CallTo(() => avisos.Resuelta(A<NotificacionResueltaDTO>.That.Matches(x => x.Resultado == "SinRespuesta"))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Vencer_YaContestada_NoHaceNada()
        {
            AmpliacionPedidoDTO contestada = Pendiente(Ahora.AddHours(-2));
            contestada.Estado = EstadosAmpliacionPedido.ENTRA;
            A.CallTo(() => repositorio.Leer(12)).Returns(Task.FromResult(contestada));

            Assert.IsNull(await Servicio().Vencer(12));
            A.CallTo(() => paso.Pasar(A<AmpliacionPedidoDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Comprobador_SoloLasPendientes()
        {
            A.CallTo(() => repositorio.Leer(12)).Returns(Task.FromResult(Pendiente()));
            AmpliacionPedidoDTO contestada = Pendiente();
            contestada.Id = 13;
            contestada.Estado = EstadosAmpliacionPedido.NO_ENTRA;
            A.CallTo(() => repositorio.Leer(13)).Returns(Task.FromResult(contestada));

            ISet<string> pendientes = await new ComprobadorAmpliacionesPendientes(repositorio).Pendientes(new[] { "12", "13", "x" });

            CollectionAssert.AreEquivalent(new[] { "12" }, pendientes.ToList());
        }

        // Regresión del caso 931007 por el PUT entero: a las 13:33 NestoApp (sin elección) añade el 39284 → 409 antes de tocar nada
        [TestMethod]
        public async Task PutPedidoVenta_Caso931007SinEleccion_409AntesDeTocarNada()
        {
            NVEntities db = A.Fake<NVEntities>();
            IQueryable<CabPedidoVta> cabeceras = new List<CabPedidoVta> { new CabPedidoVta { Empresa = "1", Número = 931007 } }.AsQueryable();
            System.Data.Entity.DbSet<CabPedidoVta> dbSet = A.Fake<System.Data.Entity.DbSet<CabPedidoVta>>(o => o.Implements<IQueryable<CabPedidoVta>>()
                .Implements<System.Data.Entity.Infrastructure.IDbAsyncEnumerable<CabPedidoVta>>());
            A.CallTo(() => ((IQueryable<CabPedidoVta>)dbSet).Provider).Returns(cabeceras.Provider);
            A.CallTo(() => ((IQueryable<CabPedidoVta>)dbSet).Expression).Returns(cabeceras.Expression);
            A.CallTo(() => ((IQueryable<CabPedidoVta>)dbSet).ElementType).Returns(cabeceras.ElementType);
            A.CallTo(() => ((IQueryable<CabPedidoVta>)dbSet).GetEnumerator()).ReturnsLazily(() => cabeceras.GetEnumerator());
            A.CallTo(() => db.CabPedidoVtas).Returns(dbSet);
            var controller = new NestoAPI.Controllers.PedidosVentaController(db) { ServicioAmpliacionEnPreparacion = Servicio() };

            await Assert.ThrowsExceptionAsync<PedidoEnPreparacionException>(() => controller.PutPedidoVenta(Put()));

            A.CallTo(() => db.Modificaciones).MustNotHaveHappened();
            A.CallTo(() => db.SaveChanges()).MustNotHaveHappened();
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        private class ControladorFalso : ApiController
        {
        }
    }

    /// <summary>Caso 931007: el pie del correo ya no dice «Con stock en Algete (puede salir el…)» de lo añadido tras sacar el picking.</summary>
    [TestClass]
    public class CorreoAmpliacionPedidoEnPreparacionTests
    {
        private static FechaEntregaAgenciaDTO Fecha(string aplica) => new FechaEntregaAgenciaDTO
        {
            FechaEntregaAgencia = new DateTime(2026, 11, 18),
            Aplica = aplica,
            Desglose = new List<PartidaFechaEntregaAgenciaDTO>
            {
                new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_PICKING, Producto = "40010", Unidades = 11, PuedeSalirEl = new DateTime(2026, 11, 18) },
                new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_ALGETE, Producto = "39284", Unidades = 1, PuedeSalirEl = new DateTime(2026, 11, 19) }
            }
        };

        [TestMethod]
        public void PedidoAAlmacen_PendienteDeQueAlmacenDigaSiEntra()
        {
            string html = GestorPresupuestos.GenerarHtmlFechaEntregaAgencia(Fecha(FechaEntregaAgenciaDTO.APLICA_PRIMERA), false, true,
                new List<LineaPedidoVentaDTO>(), 6, false, new HashSet<string>(new[] { "39284" }));

            StringAssert.Contains(html, "Añadido después de sacar el picking (pendiente de que Almacén diga si entra en el envío de hoy)");
            Assert.IsFalse(html.Contains("Con stock en Algete"), html);
        }

        [TestMethod]
        public void TodoJuntoConPickingYAlgoSinPicking_SaleAparte()
        {
            string html = GestorPresupuestos.GenerarHtmlFechaEntregaAgencia(Fecha(FechaEntregaAgenciaDTO.APLICA_COMPLETA), false, true,
                new List<LineaPedidoVentaDTO>(), 6);

            StringAssert.Contains(html, "Añadido después de sacar el picking (sale aparte; puede salir el");
        }

        [TestMethod]
        public void SinTodoJuntoNiPeticion_ComoSiempre()
        {
            string html = GestorPresupuestos.GenerarHtmlFechaEntregaAgencia(Fecha(FechaEntregaAgenciaDTO.APLICA_PRIMERA), false, true,
                new List<LineaPedidoVentaDTO>(), 6);

            StringAssert.Contains(html, "Con stock en Algete");
            Assert.IsFalse(html.Contains("Añadido después de sacar el picking"));
        }

        [TestMethod]
        public void SinNadaEnElPicking_NoHayAnadidos()
        {
            var desglose = new List<PartidaFechaEntregaAgenciaDTO>
            {
                new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_ALGETE, Producto = "39284", Unidades = 1 }
            };

            Assert.AreEqual(0, GestorPresupuestos.PartidasAnadidasTrasElPicking(desglose, true, new HashSet<string> { "39284" }).Count);
        }
    }
}
