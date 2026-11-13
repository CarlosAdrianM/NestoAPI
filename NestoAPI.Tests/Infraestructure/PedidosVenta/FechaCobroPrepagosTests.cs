using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Http.Results;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Tests.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#860 (corte 1): la fecha real del cobro del prepago (Prepagos.FechaCobro, fuera del EDMX). Null = como hoy y,
    /// al modificar, no se toca lo guardado; con hora, solo el día; posterior a hoy, 400. Sin la columna, todo como antes.
    /// </summary>
    [TestClass]
    public class FechaCobroPrepagosTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 13);

        [TestCleanup]
        public void Limpiar()
        {
            ColumnasOpcionales.ForzarParaTests = null;
        }

        private static PrepagoDTO Prepago(DateTime? fechaCobro, string factura = null)
        {
            return new PrepagoDTO
            {
                Importe = 100m,
                CuentaContable = "57200001",
                ConceptoAdicional = "Transferencia",
                Factura = factura,
                FechaCobro = fechaCobro
            };
        }

        #region Reglas puras

        [TestMethod]
        public void Normalizar_ConHora_SeQuedaSoloElDia()
        {
            Assert.AreEqual(new DateTime(2026, 11, 12), ReglasFechaCobroPrepago.Normalizar(new DateTime(2026, 11, 12, 18, 45, 10)));
        }

        [TestMethod]
        public void Normalizar_Null_SigueSiendoNull()
        {
            Assert.IsNull(ReglasFechaCobroPrepago.Normalizar(null));
        }

        [TestMethod]
        public void MotivoRechazo_FechaPosteriorAHoy_DaElMensajeDeUsted()
        {
            string motivo = ReglasFechaCobroPrepago.MotivoRechazo(new[] { Prepago(HOY.AddDays(1)) }, HOY);

            Assert.AreEqual("La fecha de cobro del prepago no puede ser posterior a hoy.", motivo);
        }

        [TestMethod]
        public void MotivoRechazo_HoyConHoraYDiasAnteriores_Valen()
        {
            Assert.IsNull(ReglasFechaCobroPrepago.MotivoRechazo(new[]
            {
                Prepago(HOY.AddHours(23).AddMinutes(59)),
                Prepago(HOY.AddDays(-1)),
                Prepago(HOY.AddDays(-40))
            }, HOY.AddHours(9)));
        }

        [TestMethod]
        public void MotivoRechazo_SinFechaOSinPrepagos_NoRechaza()
        {
            Assert.IsNull(ReglasFechaCobroPrepago.MotivoRechazo(new[] { Prepago(null) }, HOY));
            Assert.IsNull(ReglasFechaCobroPrepago.MotivoRechazo(new List<PrepagoDTO>(), HOY));
            Assert.IsNull(ReglasFechaCobroPrepago.MotivoRechazo(new PrepagoDTO[] { null }, HOY));
            Assert.IsNull(ReglasFechaCobroPrepago.MotivoRechazo(null, HOY));
        }

        [TestMethod]
        public void MotivoRechazo_UnoFuturoEntreVarios_Rechaza()
        {
            Assert.IsNotNull(ReglasFechaCobroPrepago.MotivoRechazo(new[] { Prepago(null), Prepago(HOY), Prepago(HOY.AddDays(3)) }, HOY));
        }

        #endregion

        #region Fechas a guardar

        [TestMethod]
        public void Apuntar_SinFecha_NoSeApunta()
        {
            FechasCobroPrepagosAGuardar fechas = new FechasCobroPrepagosAGuardar();

            fechas.Apuntar(new Prepago { Id = 7 }, null);
            fechas.Apuntar(null, HOY);

            Assert.IsFalse(fechas.HayAlgo, "Un prepago que llega sin fecha no toca la guardada");
            Assert.AreEqual(0, fechas.PorId().Count);
        }

        [TestMethod]
        public void PorId_ConHora_SoloElDiaYPorElIdQueDaElGuardado()
        {
            FechasCobroPrepagosAGuardar fechas = new FechasCobroPrepagosAGuardar();
            Prepago nuevo = new Prepago();
            fechas.Apuntar(nuevo, new DateTime(2026, 11, 10, 17, 30, 0));
            fechas.Apuntar(new Prepago { Id = 5 }, new DateTime(2026, 11, 11));

            // El nuevo aún no tiene Id: hasta que SaveChanges no se lo da, se queda fuera
            Assert.AreEqual(1, fechas.PorId().Count);

            nuevo.Id = 9;
            Dictionary<int, DateTime> porId = fechas.PorId();
            Assert.AreEqual(2, porId.Count);
            Assert.AreEqual(new DateTime(2026, 11, 10), porId[9]);
            Assert.AreEqual(new DateTime(2026, 11, 11), porId[5]);
        }

        #endregion

        #region Repositorio sin la columna

        [TestMethod]
        public void Repositorio_SinColumna_LeerVacioYGuardarNoHaceNada()
        {
            ColumnasOpcionales.ForzarParaTests = false;
            RepositorioFechaCobroPrepagos repositorio = new RepositorioFechaCobroPrepagos(null);

            Assert.IsFalse(repositorio.ColumnaDisponible());
            Assert.AreEqual(0, repositorio.Leer("1", 930000).Count);
            // Con db null, si intentara escribir explotaría
            repositorio.Guardar(new Dictionary<int, DateTime> { { 1, HOY } });
        }

        #endregion

        #region Controlador

        [TestMethod]
        public void GuardarFechasCobroPrepagos_SinNadaApuntado_NoLlamaAlRepositorio()
        {
            IRepositorioFechaCobroPrepagos repositorio = A.Fake<IRepositorioFechaCobroPrepagos>();
            PedidosVentaController controller = new PedidosVentaController(A.Fake<NVEntities>()) { RepositorioFechaCobroPrepagos = repositorio };

            controller.GuardarFechasCobroPrepagos(930000, new FechasCobroPrepagosAGuardar());
            controller.GuardarFechasCobroPrepagos(930000, null);

            A.CallTo(() => repositorio.Guardar(A<IDictionary<int, DateTime>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void GuardarFechasCobroPrepagos_ConFecha_LaGuardaPorId()
        {
            IRepositorioFechaCobroPrepagos repositorio = A.Fake<IRepositorioFechaCobroPrepagos>();
            PedidosVentaController controller = new PedidosVentaController(A.Fake<NVEntities>()) { RepositorioFechaCobroPrepagos = repositorio };
            FechasCobroPrepagosAGuardar fechas = new FechasCobroPrepagosAGuardar();
            fechas.Apuntar(new Prepago { Id = 42 }, new DateTime(2026, 11, 12, 10, 0, 0));

            controller.GuardarFechasCobroPrepagos(930000, fechas);

            A.CallTo(() => repositorio.Guardar(A<IDictionary<int, DateTime>>.That.Matches(d =>
                d.Count == 1 && d[42] == new DateTime(2026, 11, 12)))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void GuardarFechasCobroPrepagos_SiFallaElRepositorio_NoRompeElPedido()
        {
            IRepositorioFechaCobroPrepagos repositorio = A.Fake<IRepositorioFechaCobroPrepagos>();
            A.CallTo(() => repositorio.Guardar(A<IDictionary<int, DateTime>>._)).Throws(new InvalidOperationException("caída"));
            PedidosVentaController controller = new PedidosVentaController(A.Fake<NVEntities>()) { RepositorioFechaCobroPrepagos = repositorio };
            FechasCobroPrepagosAGuardar fechas = new FechasCobroPrepagosAGuardar();
            fechas.Apuntar(new Prepago { Id = 42 }, HOY);

            controller.GuardarFechasCobroPrepagos(930000, fechas);
        }

        [TestMethod]
        public async Task PostPedidoVenta_FechaCobroPosteriorAHoy_DevuelveBadRequestAntesDeCrearNada()
        {
            NVEntities db = A.Fake<NVEntities>();
            PedidosVentaController controller = new PedidosVentaController(db);
            PedidoVentaDTO pedido = new PedidoVentaDTO { empresa = "1", cliente = "15191", contacto = "0" };
            pedido.Prepagos.Add(Prepago(DateTime.Today.AddDays(1)));

            var resultado = await controller.PostPedidoVenta(pedido);

            var badRequest = resultado as BadRequestErrorMessageResult;
            Assert.IsNotNull(badRequest, "Una fecha de cobro futura tiene que dar 400");
            Assert.AreEqual(ReglasFechaCobroPrepago.MENSAJE_FECHA_FUTURA, badRequest.Message);
            A.CallTo(() => db.CabPedidoVtas).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PutPedidoVenta_FechaCobroPosteriorAHoy_DevuelveBadRequestSinLeerElPedido()
        {
            NVEntities db = A.Fake<NVEntities>();
            PedidosVentaController controller = new PedidosVentaController(db);
            PedidoVentaDTO pedido = new PedidoVentaDTO { empresa = "1", numero = 930000, cliente = "15191", contacto = "0" };
            pedido.Prepagos.Add(Prepago(DateTime.Today.AddDays(1)));

            var resultado = await controller.PutPedidoVenta(pedido);

            var badRequest = resultado as BadRequestErrorMessageResult;
            Assert.IsNotNull(badRequest, "Una fecha de cobro futura tiene que dar 400 antes de tocar el pedido");
            Assert.AreEqual(ReglasFechaCobroPrepago.MENSAJE_FECHA_FUTURA, badRequest.Message);
            A.CallTo(() => db.CabPedidoVtas).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PutPedidoVenta_PrepagoYaFacturadoConFechaFutura_NoSeMira()
        {
            // Los prepagos facturados no se escriben en el PUT: su fecha no se valida (sigue adelante y, sin pedido, 404)
            NVEntities db = A.Fake<NVEntities>();
            PedidosVentaController controller = new PedidosVentaController(db);
            PedidoVentaDTO pedido = new PedidoVentaDTO { empresa = "1", numero = 930000, cliente = "15191", contacto = "0" };
            pedido.Prepagos.Add(Prepago(DateTime.Today.AddDays(1), factura: "NV26/000001"));

            object resultado = await SinExcepciones(() => controller.PutPedidoVenta(pedido));

            Assert.IsNotInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => db.CabPedidoVtas).MustHaveHappened();
        }

        private static async Task<object> SinExcepciones(Func<Task<System.Web.Http.IHttpActionResult>> accion)
        {
            try
            {
                return await accion();
            }
            catch (Exception ex)
            {
                // Con la BD falsa puede acabar en una excepción al leer el pedido: lo que importa es que pasó la validación
                return ex;
            }
        }

        #endregion
    }
}
