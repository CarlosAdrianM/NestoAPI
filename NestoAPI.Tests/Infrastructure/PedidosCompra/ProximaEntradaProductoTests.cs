using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PedidosCompra;
using NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PedidosCompra
{
    /// <summary>NestoAPI#869: la próxima entrada de compra de un producto en la ficha (pedido en el aire, orientativa o nada).</summary>
    [TestClass]
    public class ProximaEntradaProductoTests
    {
        // Lunes 16/11/2026
        private static readonly DateTime HOY = new DateTime(2026, 11, 16);
        private const string PRODUCTO = "17404";
        private const string PROVEEDOR = "123";

        private static PlazosProveedores Plazos(bool conPropio = true, bool conGenerico = true)
        {
            var filas = new List<PlazoProveedor>();
            if (conPropio)
            {
                // P80 12 días, pide cada 14, el último hace 7 → próximo pedido el 23/11, llegaría el 05/12; pedido hoy, el 28/11.
                filas.Add(new PlazoProveedor
                {
                    Empresa = "1", Proveedor = PROVEEDOR, Recepciones = 10, DiasMediana = 9, DiasP80 = 12,
                    IntervaloPedidosMediano = 14, UltimoPedido = new DateTime(2026, 11, 9)
                });
            }
            if (conGenerico)
            {
                filas.Add(new PlazoProveedor { Empresa = "1", Proveedor = CalculoPlazosProveedores.PROVEEDOR_GENERICO, Recepciones = 500, DiasP80 = 20 });
            }
            return new PlazosProveedores(filas);
        }

        private static readonly Dictionary<string, string> NOMBRES = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { PROVEEDOR, "COSMÉTICA EJEMPLO, S.L." }
        };

        private static LineaCompraEnElAire Linea(int pedido, DateTime? fecha, int cantidad = 6, string usuario = "Compras", string empresa = "1") =>
            new LineaCompraEnElAire { Empresa = empresa, Pedido = pedido, Proveedor = PROVEEDOR, FechaRecepcion = fecha, Cantidad = cantidad, Usuario = usuario };

        private static ProximaEntradaProductoDTO Calcular(IEnumerable<LineaCompraEnElAire> lineas, IEnumerable<CorreoAplicadoPedidoCompra> correos = null,
            PlazosProveedores plazos = null, string habitual = PROVEEDOR) =>
            ReglaProximaEntrada.Calcular(HOY, PRODUCTO, "ALG", lineas, correos, plazos ?? Plazos(), habitual, NOMBRES);

        // ---------- (1) pedido en el aire ----------

        [TestMethod]
        public void ProximaEntrada_PedidoConCorreoVigente_PracticamenteSegura()
        {
            var correos = new List<CorreoAplicadoPedidoCompra>
            {
                new CorreoAplicadoPedidoCompra { Empresa = "1", Pedido = 220856, FechaRecepcionAplicada = new DateTime(2026, 11, 27), Agencia = "UPS", AplicadoEl = HOY }
            };

            ProximaEntradaProductoDTO r = Calcular(new[] { Linea(220856, new DateTime(2026, 11, 27), usuario: ServicioAplicacionCorreoUsuario()) }, correos);

            Assert.AreEqual(ProximaEntradaProductoDTO.TIPO_PEDIDO, r.Tipo);
            Assert.AreEqual(new DateTime(2026, 11, 27), r.Fecha);
            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.CERTEZA_CASI_SEGURA, r.Certeza);
            Assert.AreEqual("Prácticamente segura", r.EtiquetaCerteza);
            Assert.IsTrue(r.PorCorreoSeguimiento);
            Assert.AreEqual("UPS", r.AgenciaSeguimiento);
            Assert.AreEqual("Pedido 220856: 27/11 (fecha prácticamente segura: ya enviado por UPS)", r.Texto);
            Assert.AreEqual("COSMÉTICA EJEMPLO, S.L.", r.NombreProveedorPedido);
        }

        private static string ServicioAplicacionCorreoUsuario() =>
            NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento.ServicioAplicacionCorreosCompras.USUARIO_LINEAS_AUTOMATICO;

        [TestMethod]
        public void ProximaEntrada_CorreoConOtraFecha_NoMandaYEsAproximada()
        {
            // Compras cambió la fecha después del correo: el correo ya no manda (ReglaSeguimientoAgencia, como en el motor).
            var correos = new List<CorreoAplicadoPedidoCompra>
            {
                new CorreoAplicadoPedidoCompra { Empresa = "1", Pedido = 220856, FechaRecepcionAplicada = new DateTime(2026, 11, 20), Agencia = "UPS" }
            };

            ProximaEntradaProductoDTO r = Calcular(new[] { Linea(220856, new DateTime(2026, 11, 27)) }, correos);

            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.CERTEZA_APROXIMADA, r.Certeza);
            Assert.IsFalse(r.PorCorreoSeguimiento);
            Assert.AreEqual("Pedido 220856: 27/11 (fecha aproximada, aún sin seguimiento de la agencia)", r.Texto);
        }

        [TestMethod]
        public void ProximaEntrada_FechaRecalculadaPorVencida_LoDice()
        {
            ProximaEntradaProductoDTO r = Calcular(new[] { Linea(220856, new DateTime(2026, 11, 27), usuario: RastroFechaRecalculada.USUARIO) });

            Assert.IsTrue(r.Recalculada);
            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.CERTEZA_APROXIMADA, r.Certeza);
            Assert.AreEqual("Pedido 220856: 27/11 (recalculada, la prevista había pasado; fecha aproximada)", r.Texto);
        }

        [TestMethod]
        public void ProximaEntrada_PrevistaPasadaSinRecalcular_LoDice()
        {
            ProximaEntradaProductoDTO r = Calcular(new[] { Linea(220856, new DateTime(2026, 11, 10)) });

            Assert.IsTrue(r.PrevistaPasada);
            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.CERTEZA_APROXIMADA, r.Certeza);
            Assert.AreEqual("Pedido 220856: 10/11 (la prevista ya ha pasado y no hay noticias)", r.Texto);
        }

        [TestMethod]
        public void ProximaEntrada_VariosPedidos_ElDeFechaMasTempranaYCuentaLosDemas()
        {
            ProximaEntradaProductoDTO r = Calcular(new[]
            {
                Linea(220900, new DateTime(2026, 12, 10), 10),
                Linea(220856, new DateTime(2026, 11, 27), 4),
                Linea(220856, new DateTime(2026, 11, 27), 2),
                Linea(220950, null, 3)
            });

            Assert.AreEqual(220856, r.Pedido);
            Assert.AreEqual(6, r.CantidadPedido);
            Assert.AreEqual(19, r.CantidadEnElAire);
            Assert.AreEqual(3, r.PedidosEnElAire);
            Assert.AreEqual("Pedido 220856: 27/11 (fecha aproximada, aún sin seguimiento de la agencia). Hay 3 pedidos en camino (19 unidades)", r.Texto);
        }

        [TestMethod]
        public void ProximaEntrada_PedidoSinFecha_SinCerteza()
        {
            ProximaEntradaProductoDTO r = Calcular(new[] { Linea(220950, null) });

            Assert.AreEqual(ProximaEntradaProductoDTO.TIPO_PEDIDO, r.Tipo);
            Assert.IsNull(r.Fecha);
            Assert.IsNull(r.Certeza);
            Assert.AreEqual("Pedido 220950: sin fecha de recepción", r.Texto);
        }

        [TestMethod]
        public void ProximaEntrada_FechaDeOtroAnio_LlevaElAnio()
        {
            ProximaEntradaProductoDTO r = Calcular(new[] { Linea(220856, new DateTime(2027, 1, 18)) });

            Assert.AreEqual("Pedido 220856: 18/01/2027 (fecha aproximada, aún sin seguimiento de la agencia)", r.Texto);
        }

        // ---------- (2) sin pedido: orientativa ----------

        [TestMethod]
        public void ProximaEntrada_SinPedido_OrientativaConLaReglaDel853()
        {
            ProximaEntradaProductoDTO r = Calcular(new LineaCompraEnElAire[0]);

            Assert.AreEqual(ProximaEntradaProductoDTO.TIPO_ORIENTATIVA, r.Tipo);
            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.CERTEZA_ORIENTATIVA, r.Certeza);
            Assert.AreEqual("Orientativa", r.EtiquetaCerteza);
            // La misma fecha que la regla que usa el motor
            DateTime? regla = ReglaFechaOrientativaSinPedir.LlegadaEstimada(HOY, Plazos().DeProveedor(PROVEEDOR), Plazos().Generico);
            Assert.AreEqual(regla, r.Fecha);
            Assert.AreEqual(new DateTime(2026, 12, 5), r.Fecha);
            Assert.AreEqual(new DateTime(2026, 11, 28), r.FechaSiSePidieraHoy);
            Assert.AreEqual(new DateTime(2026, 11, 23), r.FechaProximoPedidoHabitual);
            Assert.AreEqual(14, r.CadenciaDias);
            Assert.AreEqual(12, r.PlazoDias);
            Assert.IsTrue(r.PlazoPropio);
            Assert.AreEqual(PROVEEDOR, r.ProveedorHabitual);
            Assert.AreEqual("Orientativa: no hay pedido; si se pidiera hoy, llegaría hacia el 28/11. El próximo pedido habitual a COSMÉTICA EJEMPLO, S.L. " +
                "sería hacia el 23/11 y llegaría hacia el 05/12 (orientativo, no es un compromiso).", r.Texto);
        }

        [TestMethod]
        public void ProximaEntrada_ProveedorSinFilaPropia_SoloSiSePidieraHoyConElGenerico()
        {
            ProximaEntradaProductoDTO r = Calcular(new LineaCompraEnElAire[0], plazos: Plazos(conPropio: false));

            Assert.AreEqual(ProximaEntradaProductoDTO.TIPO_ORIENTATIVA, r.Tipo);
            Assert.AreEqual(new DateTime(2026, 12, 6), r.Fecha);
            Assert.IsNull(r.FechaProximoPedidoHabitual);
            Assert.IsFalse(r.PlazoPropio);
            Assert.AreEqual("Orientativa: no hay pedido; si se pidiera hoy, llegaría hacia el 06/12 (orientativo, no es un compromiso).", r.Texto);
        }

        // ---------- (3) sin datos ----------

        [TestMethod]
        public void ProximaEntrada_SinProveedorHabitual_SinDatosConMotivo()
        {
            ProximaEntradaProductoDTO r = Calcular(new LineaCompraEnElAire[0], habitual: null);

            Assert.AreEqual(ProximaEntradaProductoDTO.TIPO_SIN_DATOS, r.Tipo);
            Assert.IsNull(r.Fecha);
            Assert.AreEqual(ReglaProximaEntrada.MOTIVO_SIN_PROVEEDOR, r.Motivo);
            Assert.AreEqual(r.Motivo, r.Texto);
        }

        [TestMethod]
        public void ProximaEntrada_SinPlazos_SinDatos()
        {
            ProximaEntradaProductoDTO r = ReglaProximaEntrada.Calcular(HOY, PRODUCTO, "ALG", null, null, null, PROVEEDOR, NOMBRES);

            Assert.AreEqual(ProximaEntradaProductoDTO.TIPO_SIN_DATOS, r.Tipo);
            Assert.AreEqual(ReglaProximaEntrada.MOTIVO_SIN_PLAZOS, r.Motivo);
        }

        [TestMethod]
        public void ProximaEntrada_ProveedorSinPlazoNiGenerico_SinDatosConSuNombre()
        {
            var plazos = new PlazosProveedores(new[] { new PlazoProveedor { Empresa = "1", Proveedor = "999", Recepciones = 10, DiasP80 = 5 } });

            ProximaEntradaProductoDTO r = Calcular(new LineaCompraEnElAire[0], plazos: plazos);

            Assert.AreEqual(ProximaEntradaProductoDTO.TIPO_SIN_DATOS, r.Tipo);
            Assert.AreEqual("No hay pedido al proveedor y no hay datos de cuánto tarda COSMÉTICA EJEMPLO, S.L.", r.Motivo);
        }

        [TestMethod]
        public void ProximaEntrada_ConPedido_TraeTambienLaOrientativaDelProveedorHabitual()
        {
            ProximaEntradaProductoDTO r = Calcular(new[] { Linea(220856, new DateTime(2026, 11, 27)) });

            Assert.AreEqual(ProximaEntradaProductoDTO.TIPO_PEDIDO, r.Tipo);
            Assert.AreEqual(new DateTime(2026, 11, 28), r.FechaSiSePidieraHoy);
        }

        // ---------- servicio y controlador ----------

        private IRepositorioProximaEntrada repositorio;
        private ILectorCertezaFechaEntrega lector;

        private ServicioProximaEntradaProducto Servicio()
        {
            repositorio = A.Fake<IRepositorioProximaEntrada>();
            lector = A.Fake<ILectorCertezaFechaEntrega>();
            A.CallTo(() => repositorio.EsFicticio(A<string>._, A<string>._)).Returns(false);
            A.CallTo(() => repositorio.EnElAire(A<string>._, A<string>._, A<string>._)).Returns(new List<LineaCompraEnElAire>());
            A.CallTo(() => repositorio.NombresProveedores(A<string>._, A<IEnumerable<string>>._)).Returns(NOMBRES);
            A.CallTo(() => lector.Plazos(A<string>._)).Returns(Plazos());
            A.CallTo(() => lector.ProveedorHabitual(A<string>._, A<IEnumerable<string>>._))
                .Returns(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { PRODUCTO, PROVEEDOR } });
            return new ServicioProximaEntradaProducto(repositorio, lector, () => HOY);
        }

        [TestMethod]
        public void ServicioProximaEntrada_EmpresaEspejoYAlmacenPorDefecto()
        {
            ServicioProximaEntradaProducto servicio = Servicio();

            ProximaEntradaProductoDTO r = servicio.Calcular("3", " 17404 ", null);

            Assert.AreEqual("ALG", r.Almacen);
            Assert.AreEqual(ProximaEntradaProductoDTO.TIPO_ORIENTATIVA, r.Tipo);
            A.CallTo(() => repositorio.EnElAire("1", "17404", "ALG")).MustHaveHappenedOnceExactly();
            A.CallTo(() => lector.Plazos("1")).MustHaveHappened();
            // Sin líneas no se leen correos
            A.CallTo(() => lector.CorreosAplicados(A<IEnumerable<int>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void ServicioProximaEntrada_ConLineas_LeeLosCorreosDeEsosPedidos()
        {
            ServicioProximaEntradaProducto servicio = Servicio();
            A.CallTo(() => repositorio.EnElAire(A<string>._, A<string>._, A<string>._)).Returns(new List<LineaCompraEnElAire> { Linea(220856, new DateTime(2026, 11, 27)) });
            A.CallTo(() => lector.CorreosAplicados(A<IEnumerable<int>>._)).Returns(new List<CorreoAplicadoPedidoCompra>
            {
                new CorreoAplicadoPedidoCompra { Empresa = "1", Pedido = 220856, FechaRecepcionAplicada = new DateTime(2026, 11, 27), Agencia = "GLS" }
            });

            ProximaEntradaProductoDTO r = servicio.Calcular("1", PRODUCTO, "alg");

            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.CERTEZA_CASI_SEGURA, r.Certeza);
            A.CallTo(() => lector.CorreosAplicados(A<IEnumerable<int>>.That.Matches(p => p.Single() == 220856))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void ServicioProximaEntrada_ProductoQueNoExiste_Null()
        {
            ServicioProximaEntradaProducto servicio = Servicio();
            A.CallTo(() => repositorio.EsFicticio(A<string>._, A<string>._)).Returns(null);

            Assert.IsNull(servicio.Calcular("1", "NOEXISTE", "ALG"));
        }

        [TestMethod]
        public void ServicioProximaEntrada_Ficticio_SinDatosSinLeerNadaMas()
        {
            ServicioProximaEntradaProducto servicio = Servicio();
            A.CallTo(() => repositorio.EsFicticio(A<string>._, A<string>._)).Returns(true);

            ProximaEntradaProductoDTO r = servicio.Calcular("1", PRODUCTO, "ALG");

            Assert.AreEqual(ProximaEntradaProductoDTO.TIPO_SIN_DATOS, r.Tipo);
            Assert.AreEqual(ReglaProximaEntrada.MOTIVO_FICTICIO, r.Motivo);
            A.CallTo(() => repositorio.EnElAire(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void ProximaEntradaController_SinProducto_400()
        {
            var controlador = new ProximaEntradaProductoController(() => Servicio());

            Assert.IsInstanceOfType(controlador.GetProximaEntrada(" "), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void ProximaEntradaController_ProductoQueNoExiste_404()
        {
            var controlador = new ProximaEntradaProductoController(() =>
            {
                ServicioProximaEntradaProducto s = Servicio();
                A.CallTo(() => repositorio.EsFicticio(A<string>._, A<string>._)).Returns(null);
                return s;
            });

            Assert.IsInstanceOfType(controlador.GetProximaEntrada("NOEXISTE"), typeof(NotFoundResult));
        }

        [TestMethod]
        public void ProximaEntradaController_Producto_200ConLaProximaEntrada()
        {
            var controlador = new ProximaEntradaProductoController(() => Servicio());

            var r = controlador.GetProximaEntrada(PRODUCTO) as OkNegotiatedContentResult<ProximaEntradaProductoDTO>;

            Assert.IsNotNull(r);
            Assert.AreEqual(ProximaEntradaProductoDTO.TIPO_ORIENTATIVA, r.Content.Tipo);
            Assert.AreEqual(new DateTime(2026, 12, 5), r.Content.Fecha);
        }
    }
}
