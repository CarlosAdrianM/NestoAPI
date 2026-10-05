using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.ChequesRegalo;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.ChequesRegalo
{
    /// <summary>
    /// NestoAPI#593 (c2): la primera factura de venta normal de cada código de cliente dentro de la ventana de la
    /// campaña genera un cheque. Todo lo que cambia de una campaña a otra (importe, fechas, mínimos, espera) es un
    /// parámetro de la campaña.
    /// </summary>
    [TestClass]
    public class ReglasChequeRegaloTests
    {
        internal static CampanaChequeRegalo Campana(decimal minimoFactura = 0, int diasEspera = 0, bool activa = true) => new CampanaChequeRegalo
        {
            Codigo = "CHEQUE50_OCT_2026",
            Empresa = "1",
            Producto = "CHEQUE50_OCT26",
            ImporteBase = 50,
            GeneraDesde = new DateTime(2026, 10, 12),
            GeneraHasta = new DateTime(2026, 10, 29),
            CanjeHasta = new DateTime(2026, 10, 31),
            MinimoFacturaQueGenera = minimoFactura,
            MinimoCanje = 250,
            DiasEsperaTrasEntrega = diasEspera,
            Activa = activa
        };

        internal static FacturaParaChequeRegalo Factura(string numero, int dia, decimal baseProducto = 100, decimal? baseTotal = null,
            string serie = "NV", bool devolucion = false, string empresa = "1", string cliente = "15000") => new FacturaParaChequeRegalo
        {
            Empresa = empresa,
            Numero = numero,
            Fecha = new DateTime(2026, 10, dia, 10, 0, 0),
            Serie = serie,
            Cliente = cliente,
            BaseProducto = baseProducto,
            BaseTotal = baseTotal ?? baseProducto,
            TieneDevolucion = devolucion
        };

        private static DecisionChequeRegalo Decidir(CampanaChequeRegalo campana, params FacturaParaChequeRegalo[] facturas)
            => ReglasChequeRegalo.Decidir(campana, "15000", clienteFinDeMes: false, facturas);

        [TestMethod]
        public void LaPrimeraFacturaNormalDeLaVentanaGenera()
        {
            DecisionChequeRegalo d = Decidir(Campana(), Factura("NV2600002", 14), Factura("NV2600001", 13));

            Assert.IsTrue(d.Genera, d.Motivo);
            Assert.AreEqual("NV2600001", d.Factura.Numero);
        }

        [TestMethod]
        public void LasFacturasFueraDeLaVentanaNoCuentan()
        {
            // El 11 es antes de empezar; el 30, después de acabar: la primera de la ventana es la del 29
            DecisionChequeRegalo d = Decidir(Campana(),
                Factura("NV2600001", 11), Factura("NV2600003", 30), Factura("NV2600002", 29));

            Assert.IsTrue(d.Genera, d.Motivo);
            Assert.AreEqual("NV2600002", d.Factura.Numero);
        }

        [TestMethod]
        public void ElUltimoDiaDeLaVentanaCuentaEntero()
        {
            FacturaParaChequeRegalo tarde = Factura("NV2600009", 29);
            tarde.Fecha = new DateTime(2026, 10, 29, 23, 59, 0);

            Assert.IsTrue(Decidir(Campana(), tarde).Genera);
        }

        [TestMethod]
        public void SinFacturasEnLaVentanaNoGenera()
        {
            DecisionChequeRegalo d = Decidir(Campana(), Factura("NV2600001", 11));

            Assert.IsFalse(d.Genera);
        }

        [TestMethod]
        public void UnaCampanaInactivaNoGenera()
        {
            Assert.IsFalse(Decidir(Campana(activa: false), Factura("NV2600001", 13)).Genera);
        }

        [TestMethod]
        public void LasRectificativasNoGeneran_YLaSiguienteNormalSi()
        {
            DecisionChequeRegalo d = Decidir(Campana(),
                Factura("RV2600001", 13, baseProducto: -20, serie: "RV"), Factura("NV2600001", 14));

            Assert.AreEqual("NV2600001", d.Factura.Numero);
        }

        [TestMethod]
        public void UnaFacturaACeroONegativaNoGenera()
        {
            Assert.IsFalse(Decidir(Campana(), Factura("NV2600001", 13, baseProducto: 0)).Genera);
            Assert.IsFalse(Decidir(Campana(), Factura("NV2600002", 13, baseProducto: 40, baseTotal: -5)).Genera);
        }

        [TestMethod]
        public void UnCambioConDevolucionNoGenera()
        {
            Assert.IsFalse(Decidir(Campana(), Factura("NV2600001", 13, devolucion: true)).Genera);
        }

        [TestMethod]
        public void UnaFacturaSoloDePortesNoGenera()
        {
            // Las cuentas contables (portes, reembolso, cuotas, reparaciones) ni generan ni cuentan
            Assert.IsFalse(Decidir(Campana(), Factura("NV2600001", 13, baseProducto: 0, baseTotal: 6)).Genera);
        }

        [TestMethod]
        public void LaSerieGBSiGenera()
        {
            Assert.IsTrue(Decidir(Campana(), Factura("GB2600001", 13, serie: "GB", empresa: "3")).Genera);
        }

        [TestMethod]
        public void ElMinimoDeLaFacturaSeMiraEnLaBaseDeProducto()
        {
            Assert.IsFalse(Decidir(Campana(minimoFactura: 50), Factura("NV2600001", 13, baseProducto: 49.99m, baseTotal: 60)).Genera);
            Assert.IsTrue(Decidir(Campana(minimoFactura: 50), Factura("NV2600002", 13, baseProducto: 50)).Genera);
        }

        [TestMethod]
        public void SiLaPrimeraFacturaNormalNoLlegaAlMinimo_NoGeneraAunqueLaSegundaSiLlegue()
        {
            // «La primera factura de venta normal» es la que decide: no se busca otra que llegue
            DecisionChequeRegalo d = Decidir(Campana(minimoFactura: 50),
                Factura("NV2600001", 13, baseProducto: 20), Factura("NV2600002", 14, baseProducto: 300));

            Assert.IsFalse(d.Genera);
            StringAssert.Contains(d.Motivo, "NV2600001");
        }

        [TestMethod]
        public void LosClientesGenericosNoGeneran()
        {
            foreach (string generico in new[] { "10458", "31517", "9500", "31794", "32624" })
            {
                DecisionChequeRegalo d = ReglasChequeRegalo.Decidir(Campana(), generico, false, new[] { Factura("NV2600001", 13, cliente: generico) });
                Assert.IsFalse(d.Genera, generico);
            }
        }

        [TestMethod]
        public void LosClientesDeFinDeMesNoGeneran()
        {
            DecisionChequeRegalo d = ReglasChequeRegalo.Decidir(Campana(), "15000", clienteFinDeMes: true, new[] { Factura("NV2600001", 13) });

            Assert.IsFalse(d.Genera);
        }

        [TestMethod]
        public void SinEsperaSeActivaAlGenerar_ConEsperaQuedaPendiente()
        {
            var cuando = new DateTime(2026, 10, 13, 9, 0, 0);

            Assert.AreEqual(cuando, ReglasChequeRegalo.FechaActivacion(Campana(diasEspera: 0), cuando));
            Assert.IsNull(ReglasChequeRegalo.FechaActivacion(Campana(diasEspera: 3), cuando));
        }

        [TestMethod]
        public void ElAvisoDiceImporteMinimoYFecha()
        {
            string aviso = ReglasChequeRegalo.TextoAviso(Campana(), "15000");

            StringAssert.Contains(aviso, "50");
            StringAssert.Contains(aviso, "250");
            StringAssert.Contains(aviso, "31/10/2026");
        }
    }

    [TestClass]
    public class GeneradorChequesRegaloTests
    {
        private IRepositorioChequesRegalo repositorio;
        private readonly DateTime hoy = new DateTime(2026, 10, 13);

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioChequesRegalo>();
            A.CallTo(() => repositorio.LeerCampanasActivas())
                .Returns(Task.FromResult(new List<CampanaChequeRegalo> { ReglasChequeRegaloTests.Campana() }));
            A.CallTo(() => repositorio.InsertarCheque(A<ChequeRegaloNuevo>._)).Returns(Task.FromResult(true));
        }

        private GeneradorChequesRegalo Nuevo(bool encendido = true) => new GeneradorChequesRegalo(repositorio, () => encendido);

        private void FacturasDelCliente(params FacturaParaChequeRegalo[] facturas)
            => A.CallTo(() => repositorio.LeerFacturasDelCliente("15000", A<DateTime>._, A<DateTime>._))
                .Returns(Task.FromResult(facturas.ToList()));

        [TestMethod]
        public async Task AlFacturarLaPrimera_GeneraElChequeYAvisa()
        {
            FacturasDelCliente(ReglasChequeRegaloTests.Factura("NV2600001", 13));

            List<string> avisos = await Nuevo().GenerarPorFactura("1", "NV2600001", "15000", hoy, "Carlos");

            Assert.AreEqual(1, avisos.Count);
            A.CallTo(() => repositorio.InsertarCheque(A<ChequeRegaloNuevo>.That.Matches(c =>
                c.Campana == "CHEQUE50_OCT_2026" && c.Empresa == "1" && c.Cliente == "15000"
                && c.EmpresaFactura == "1" && c.FacturaOrigen == "NV2600001" && c.Usuario == "Carlos"
                && c.FechaActivacion != null))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ConElInterruptorApagado_NoHaceNada()
        {
            FacturasDelCliente(ReglasChequeRegaloTests.Factura("NV2600001", 13));

            List<string> avisos = await Nuevo(encendido: false).GenerarPorFactura("1", "NV2600001", "15000", hoy, "Carlos");

            Assert.AreEqual(0, avisos.Count);
            A.CallTo(() => repositorio.LeerCampanasActivas()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task FueraDeLaVentana_NoGenera()
        {
            List<string> avisos = await Nuevo().GenerarPorFactura("1", "NV2600001", "15000", new DateTime(2026, 10, 30), "Carlos");

            Assert.AreEqual(0, avisos.Count);
            A.CallTo(() => repositorio.InsertarCheque(A<ChequeRegaloNuevo>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SiYaTieneCheque_NoLoVuelveAGenerar()
        {
            A.CallTo(() => repositorio.TieneCheque("CHEQUE50_OCT_2026", "1", "15000")).Returns(Task.FromResult(true));
            FacturasDelCliente(ReglasChequeRegaloTests.Factura("NV2600002", 13));

            List<string> avisos = await Nuevo().GenerarPorFactura("1", "NV2600002", "15000", hoy, "Carlos");

            Assert.AreEqual(0, avisos.Count);
            A.CallTo(() => repositorio.InsertarCheque(A<ChequeRegaloNuevo>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SiOtraFacturaLoGeneroALaVez_NoAvisaDosVeces()
        {
            // La UNIQUE de la tabla manda: si el INSERT no entra, otro lo hizo antes
            A.CallTo(() => repositorio.InsertarCheque(A<ChequeRegaloNuevo>._)).Returns(Task.FromResult(false));
            FacturasDelCliente(ReglasChequeRegaloTests.Factura("NV2600001", 13));

            List<string> avisos = await Nuevo().GenerarPorFactura("1", "NV2600001", "15000", hoy, "Carlos");

            Assert.AreEqual(0, avisos.Count);
        }

        [TestMethod]
        public async Task SiLaPrimeraEraOtraFacturaAnterior_GeneraConEllaPeroSinAvisarEnEsta()
        {
            // Una factura de antes hecha fuera de la API (y aún sin reconciliar): el cheque es suyo
            FacturasDelCliente(ReglasChequeRegaloTests.Factura("NV2600001", 12), ReglasChequeRegaloTests.Factura("NV2600002", 13));

            List<string> avisos = await Nuevo().GenerarPorFactura("1", "NV2600002", "15000", hoy, "Carlos");

            Assert.AreEqual(0, avisos.Count);
            A.CallTo(() => repositorio.InsertarCheque(A<ChequeRegaloNuevo>.That.Matches(c => c.FacturaOrigen == "NV2600001")))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task UnClienteGenerico_NiSiquieraConsultaLaBaseDeDatos()
        {
            List<string> avisos = await Nuevo().GenerarPorFactura("1", "NV2600001", "32624", hoy, "Carlos");

            Assert.AreEqual(0, avisos.Count);
            A.CallTo(() => repositorio.LeerFacturasDelCliente(A<string>._, A<DateTime>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ElClienteDeFinDeMes_NoGenera()
        {
            A.CallTo(() => repositorio.EsClienteFinDeMes("15000")).Returns(Task.FromResult(true));
            FacturasDelCliente(ReglasChequeRegaloTests.Factura("NV2600001", 13));

            List<string> avisos = await Nuevo().GenerarPorFactura("1", "NV2600001", "15000", hoy, "Carlos");

            Assert.AreEqual(0, avisos.Count);
            A.CallTo(() => repositorio.InsertarCheque(A<ChequeRegaloNuevo>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Reconciliar_GeneraLosQueFaltan()
        {
            A.CallTo(() => repositorio.LeerClientesSinCheque(A<CampanaChequeRegalo>._))
                .Returns(Task.FromResult(new List<string> { "15000", "16000" }));
            FacturasDelCliente(ReglasChequeRegaloTests.Factura("NV2600001", 13));
            A.CallTo(() => repositorio.LeerFacturasDelCliente("16000", A<DateTime>._, A<DateTime>._))
                .Returns(Task.FromResult(new List<FacturaParaChequeRegalo> { ReglasChequeRegaloTests.Factura("RV2600001", 13, -10, serie: "RV", cliente: "16000") }));

            int generados = await Nuevo().Reconciliar(new DateTime(2026, 10, 14), "Reconciliación");

            Assert.AreEqual(1, generados);
        }

        [TestMethod]
        public async Task Reconciliar_ConElInterruptorApagado_NoHaceNada()
        {
            int generados = await Nuevo(encendido: false).Reconciliar(new DateTime(2026, 10, 14), "Reconciliación");

            Assert.AreEqual(0, generados);
            A.CallTo(() => repositorio.LeerCampanasActivas()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Reconciliar_AntesDeEmpezarLaCampana_NoMira()
        {
            int generados = await Nuevo().Reconciliar(new DateTime(2026, 10, 5), "Reconciliación");

            Assert.AreEqual(0, generados);
            A.CallTo(() => repositorio.LeerClientesSinCheque(A<CampanaChequeRegalo>._)).MustNotHaveHappened();
        }
    }

    [TestClass]
    public class ServicioFacturasChequeRegaloTests
    {
        [TestMethod]
        public async Task SiGeneraCheque_ElAvisoLlegaAQuienFactura()
        {
            IGeneradorChequesRegalo generador = A.Fake<IGeneradorChequesRegalo>();
            A.CallTo(() => generador.GenerarPorFactura("1", "NV2600001", "15000", A<DateTime>._, "Carlos"))
                .Returns(Task.FromResult(new List<string> { "Cheque regalo generado" }));
            var respuesta = new CrearFacturaResponseDTO { NumeroFactura = "NV2600001", Empresa = "1" };

            await ServicioFacturas.AnadirChequeRegalo(generador, respuesta, "15000", new DateTime(2026, 10, 13), "Carlos");

            CollectionAssert.Contains(respuesta.Avisos, "Cheque regalo generado");
        }

        [TestMethod]
        public async Task SiElGeneradorFalla_LaFacturaSigueAdelante()
        {
            IGeneradorChequesRegalo generador = A.Fake<IGeneradorChequesRegalo>();
            A.CallTo(() => generador.GenerarPorFactura(A<string>._, A<string>._, A<string>._, A<DateTime>._, A<string>._))
                .Throws(new InvalidOperationException("sin tabla"));
            var respuesta = new CrearFacturaResponseDTO { NumeroFactura = "NV2600001", Empresa = "1" };

            await ServicioFacturas.AnadirChequeRegalo(generador, respuesta, "15000", new DateTime(2026, 10, 13), "Carlos");

            Assert.AreEqual(0, respuesta.Avisos.Count);
        }
    }
}
