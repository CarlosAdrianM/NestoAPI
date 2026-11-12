using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.PedidosCompra
{
    /// <summary>NestoAPI#853 (corte 2): plazo real por proveedor, la fecha de recepción al enviar y la llegada orientativa de lo no pedido.</summary>
    [TestClass]
    public class PlazosProveedoresTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 12); // jueves

        private static RecepcionCompra Recepcion(string proveedor, int pedido, int dias, string empresa = "1") => new RecepcionCompra
        {
            Empresa = empresa,
            Proveedor = proveedor,
            Pedido = pedido,
            FechaPedido = HOY.AddDays(-30),
            FechaAlbaran = HOY.AddDays(-30 + dias)
        };

        private static PlazoProveedor Plazo(string proveedor, int recepciones, int? p80, int? intervalo = null, DateTime? ultimo = null) => new PlazoProveedor
        {
            Empresa = "1",
            Proveedor = proveedor,
            Recepciones = recepciones,
            DiasP80 = p80,
            DiasMediana = p80,
            IntervaloPedidosMediano = intervalo,
            UltimoPedido = ultimo
        };

        private static readonly PlazoProveedor GENERICO = Plazo(CalculoPlazosProveedores.PROVEEDOR_GENERICO, 2748, 11);

        #region Cálculo

        [TestMethod]
        public void Calcular_MedianaYP80PorProveedor_YLaFilaGenerica()
        {
            var recepciones = new[] { 1, 2, 2, 3, 4, 5, 9, 10, 12, 20 }.Select((d, i) => Recepcion("65", 1000 + i, d)).ToList();
            recepciones.Add(Recepcion("691", 2000, 7));

            List<PlazoProveedor> plazos = CalculoPlazosProveedores.Calcular("1", recepciones, new List<PedidoCompraDia>(), HOY);

            PlazoProveedor p65 = plazos.Single(p => p.Proveedor == "65");
            Assert.AreEqual(10, p65.Recepciones);
            Assert.AreEqual(4, p65.DiasMediana, "Rango más cercano: el 5.º de 10");
            Assert.AreEqual(10, p65.DiasP80, "El 8.º de 10");
            Assert.IsTrue(p65.TienePlazoPropio);
            Assert.IsFalse(plazos.Single(p => p.Proveedor == "691").TienePlazoPropio, "Con menos de 5 recepciones no tiene plazo propio");
            PlazoProveedor generico = plazos.Single(p => p.EsGenerico);
            Assert.AreEqual(11, generico.Recepciones);
            Assert.AreEqual(10, generico.DiasP80);
            Assert.IsFalse(generico.TienePlazoPropio, "La genérica nunca es plazo propio");
        }

        [TestMethod]
        public void Calcular_DescartaLasRecepcionesConElPedidoModificadoDespuesDelAlbaran()
        {
            var recepciones = new List<RecepcionCompra> { Recepcion("65", 1, 3), Recepcion("65", 2, -2) };

            PlazoProveedor p65 = CalculoPlazosProveedores.Calcular("1", recepciones, null, HOY).Single(p => p.Proveedor == "65");

            Assert.AreEqual(1, p65.Recepciones);
            Assert.AreEqual(3, p65.DiasP80);
        }

        [TestMethod]
        public void Calcular_LaEspejoCuentaConLaPrincipal()
        {
            var recepciones = new List<RecepcionCompra> { Recepcion("65", 1, 3, "1"), Recepcion("65", 1, 5, "3") };

            List<PlazoProveedor> plazos = CalculoPlazosProveedores.Calcular("1", recepciones, null, HOY);

            Assert.AreEqual(2, plazos.Single(p => p.Proveedor == "65").Recepciones);
            Assert.IsTrue(plazos.All(p => p.Empresa == "1"));
        }

        [TestMethod]
        public void Cadencia_MedianaDeDiasEntrePedidos_DiasDistintosYSoloElUltimoAnio()
        {
            var fechas = new[] { HOY.AddDays(-400), HOY.AddDays(-30), HOY.AddDays(-23), HOY.AddDays(-23), HOY.AddDays(-14), HOY.AddDays(-7) };

            Assert.AreEqual(7, CalculoPlazosProveedores.Cadencia(fechas, HOY), "Intervalos 7, 9, 7: mediana 7; el de hace 400 días y el repetido no cuentan");
            Assert.IsNull(CalculoPlazosProveedores.Cadencia(new[] { HOY.AddDays(-10), HOY.AddDays(-3), HOY.AddDays(-3) }, HOY), "Menos de 3 días de pedido");
        }

        [TestMethod]
        public void Calcular_ProveedorSoloConPedidos_TieneCadenciaYUltimoPedidoSinPlazo()
        {
            var pedidos = new[] { -21, -14, -7 }.Select(d => new PedidoCompraDia { Proveedor = "1353", Fecha = HOY.AddDays(d) }).ToList();

            PlazoProveedor plazo = CalculoPlazosProveedores.Calcular("1", null, pedidos, HOY).Single();

            Assert.AreEqual("1353", plazo.Proveedor);
            Assert.AreEqual(0, plazo.Recepciones);
            Assert.IsNull(plazo.DiasP80);
            Assert.AreEqual(7, plazo.IntervaloPedidosMediano);
            Assert.AreEqual(HOY.AddDays(-7), plazo.UltimoPedido);
        }

        #endregion

        #region Regla de la fecha al enviar

        [TestMethod]
        public void FechaAlEnviar_ConPlazoPropio_HoyMasSuP80()
        {
            FechaRecepcionEstimada f = ReglaFechaRecepcionEstimada.Calcular(HOY, 0, Plazo("65", 252, 4), GENERICO);

            Assert.AreEqual(HOY.AddDays(4), f.Fecha);
            Assert.AreEqual(HOY.AddDays(4), f.FechaRegla);
            Assert.AreEqual(FechaRecepcionEstimada.ORIGEN_PLAZO_PROVEEDOR, f.Origen);
        }

        [TestMethod]
        public void FechaAlEnviar_DiasEnServirMasPrudenteQueElP80_MandaCompras()
        {
            FechaRecepcionEstimada f = ReglaFechaRecepcionEstimada.Calcular(HOY, 10, Plazo("65", 252, 4), GENERICO);

            Assert.AreEqual(HOY.AddDays(10), f.Fecha);
            Assert.AreEqual(HOY.AddDays(4), f.FechaRegla, "La regla se apunta igual, para medir");
            Assert.AreEqual(FechaRecepcionEstimada.ORIGEN_DIAS_EN_SERVIR, f.Origen);
        }

        [TestMethod]
        public void FechaAlEnviar_SinPlazoPropioConDiasEnServir_LosDiasEnServir()
        {
            FechaRecepcionEstimada f = ReglaFechaRecepcionEstimada.Calcular(HOY, 3, Plazo("691", 2, 1), GENERICO);

            Assert.AreEqual(HOY.AddDays(3), f.Fecha);
            Assert.AreEqual(HOY.AddDays(11), f.FechaRegla, "Sin plazo propio, la regla es la genérica");
        }

        [TestMethod]
        public void FechaAlEnviar_ProveedorSinDatosNiDiasEnServir_ElGenerico()
        {
            FechaRecepcionEstimada f = ReglaFechaRecepcionEstimada.Calcular(HOY, 0, null, GENERICO);

            Assert.AreEqual(HOY.AddDays(11), f.Fecha);
            Assert.AreEqual(FechaRecepcionEstimada.ORIGEN_PLAZO_GENERICO, f.Origen);
        }

        [TestMethod]
        public void FechaAlEnviar_SinNingunPlazo_HoyComoAntes()
        {
            FechaRecepcionEstimada f = ReglaFechaRecepcionEstimada.Calcular(HOY.AddHours(17), 0, null, null);

            Assert.AreEqual(HOY, f.Fecha);
            Assert.IsNull(f.FechaRegla);
        }

        #endregion

        #region Llegada orientativa de lo no pedido

        [TestMethod]
        public void Orientativa_HastaElProximoPedidoMasElP80()
        {
            // Pide cada 7 días, el último hace 3: el próximo dentro de 4; tarda 6 → llega dentro de 10
            Assert.AreEqual(HOY.AddDays(10), ReglaFechaOrientativaSinPedir.LlegadaEstimada(HOY, Plazo("65", 100, 6, 7, HOY.AddDays(-3)), GENERICO));
        }

        [TestMethod]
        public void Orientativa_ElPedidoYaTocaba_ComoPocoMañana()
        {
            Assert.AreEqual(HOY.AddDays(1 + 6), ReglaFechaOrientativaSinPedir.LlegadaEstimada(HOY, Plazo("65", 100, 6, 7, HOY.AddDays(-20)), GENERICO));
        }

        [TestMethod]
        public void Orientativa_SinPlazoPropio_ConElGenerico()
        {
            Assert.AreEqual(HOY.AddDays(4 + 11), ReglaFechaOrientativaSinPedir.LlegadaEstimada(HOY, Plazo("691", 2, 1, 7, HOY.AddDays(-3)), GENERICO));
        }

        [TestMethod]
        public void Orientativa_SinCadenciaConocida_NoHay()
        {
            Assert.IsNull(ReglaFechaOrientativaSinPedir.LlegadaEstimada(HOY, Plazo("65", 100, 6, null, HOY.AddDays(-3)), GENERICO));
            Assert.IsNull(ReglaFechaOrientativaSinPedir.LlegadaEstimada(HOY, null, GENERICO));
        }

        #endregion

        #region Servicio: recálculo y fecha al enviar

        [TestMethod]
        public void Recalcular_SinTabla_NoHaceNada()
        {
            var repositorio = A.Fake<IRepositorioPlazosProveedores>();
            A.CallTo(() => repositorio.TablaPlazosExiste()).Returns(false);

            ResultadoRecalculoPlazos resultado = new ServicioPlazosProveedores(repositorio, () => HOY).Recalcular("1");

            Assert.IsFalse(resultado.Hecho);
            A.CallTo(() => repositorio.SustituirPlazos(A<string>._, A<IReadOnlyCollection<PlazoProveedor>>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.LeerRecepciones(A<string>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Recalcular_Sustituye24MesesConLaGenericaYLaEspejoComoLaPrincipal()
        {
            var repositorio = A.Fake<IRepositorioPlazosProveedores>();
            A.CallTo(() => repositorio.TablaPlazosExiste()).Returns(true);
            A.CallTo(() => repositorio.LeerRecepciones("1", HOY.AddMonths(-24)))
                .Returns(Enumerable.Range(0, 6).Select(i => Recepcion("65", 100 + i, 2 + i)).ToList());
            A.CallTo(() => repositorio.LeerPedidos("1", HOY.AddMonths(-24))).Returns(new List<PedidoCompraDia>());
            IReadOnlyCollection<PlazoProveedor> guardados = null;
            A.CallTo(() => repositorio.SustituirPlazos("1", A<IReadOnlyCollection<PlazoProveedor>>._))
                .Invokes((string e, IReadOnlyCollection<PlazoProveedor> p) => guardados = p);

            ResultadoRecalculoPlazos resultado = new ServicioPlazosProveedores(repositorio, () => HOY.AddHours(3.67)).Recalcular("3");

            Assert.IsTrue(resultado.Hecho);
            Assert.AreEqual(1, resultado.ConPlazoPropio);
            Assert.AreEqual(2, guardados.Count, "El proveedor y la genérica");
            Assert.IsTrue(guardados.Any(p => p.EsGenerico));
        }

        private static PlazosProveedores ConPlazos() => new PlazosProveedores(new[] { Plazo("65", 252, 4), Plazo("691", 2, 1), GENERICO });

        private static ServicioFechaRecepcionAlEnviar Servicio(IRepositorioPlazosProveedores repositorio, bool interruptor, PlazosProveedores plazos,
            List<Exception> errores = null)
        {
            ServicioFechaRecepcionAlEnviar.OlvidarComprobaciones();
            return new ServicioFechaRecepcionAlEnviar(() => repositorio, () => interruptor, () => HOY.AddHours(10), ex => errores?.Add(ex), _ => plazos);
        }

        [TestMethod]
        public void AlEnviar_InterruptorEncendido_PlazoDelProveedor()
        {
            FechaRecepcionEstimada f = Servicio(A.Fake<IRepositorioPlazosProveedores>(), true, ConPlazos()).Calcular("1", "65  ", 0);

            Assert.AreEqual(HOY.AddDays(4), f.Fecha);
            Assert.AreEqual(FechaRecepcionEstimada.ORIGEN_PLAZO_PROVEEDOR, f.Origen);
        }

        [TestMethod]
        public void AlEnviar_InterruptorApagado_ComoSiempreConLaReglaApuntada()
        {
            FechaRecepcionEstimada f = Servicio(A.Fake<IRepositorioPlazosProveedores>(), false, ConPlazos()).Calcular("1", "65", 2);

            Assert.AreEqual(HOY.AddDays(2), f.Fecha, "Hoy + DíasEnServir");
            Assert.AreEqual(HOY.AddDays(4), f.FechaRegla);
            Assert.AreEqual(ServicioFechaRecepcionAlEnviar.ORIGEN_COMO_ANTES, f.Origen);
        }

        [TestMethod]
        public void AlEnviar_SinTablaDePlazos_ComoSiempre()
        {
            FechaRecepcionEstimada f = Servicio(A.Fake<IRepositorioPlazosProveedores>(), true, null).Calcular("1", "65", 0);

            Assert.AreEqual(HOY, f.Fecha);
            Assert.IsNull(f.FechaRegla);
        }

        [TestMethod]
        public void AlEnviar_ProveedorSinDatos_ElGenerico()
        {
            Assert.AreEqual(HOY.AddDays(11), Servicio(A.Fake<IRepositorioPlazosProveedores>(), true, ConPlazos()).Calcular("1", "9999", 0).Fecha);
        }

        [TestMethod]
        public void AlEnviar_SiFallaNoRompeElEnvio()
        {
            var errores = new List<Exception>();
            ServicioFechaRecepcionAlEnviar.OlvidarComprobaciones();
            var servicio = new ServicioFechaRecepcionAlEnviar(() => A.Fake<IRepositorioPlazosProveedores>(), () => true, () => HOY.AddHours(10),
                errores.Add, _ => throw new InvalidOperationException("caída"));

            FechaRecepcionEstimada f = servicio.Calcular("1", "65", 1);

            Assert.AreEqual(HOY.AddDays(1), f.Fecha);
            Assert.AreEqual(1, errores.Count);
        }

        [TestMethod]
        public void Registrar_ConTabla_ApuntaElEnvioConLaReglaYSinModelo()
        {
            var repositorio = A.Fake<IRepositorioPlazosProveedores>();
            A.CallTo(() => repositorio.TablaEnviosExiste()).Returns(true);
            EnvioPedidoCompra apuntado = null;
            A.CallTo(() => repositorio.RegistrarEnvio(A<EnvioPedidoCompra>._)).Invokes((EnvioPedidoCompra e) => apuntado = e);
            ServicioFechaRecepcionAlEnviar servicio = Servicio(repositorio, false, ConPlazos());

            servicio.Registrar("1  ", 220900, "65", 2, servicio.Calcular("1", "65", 2), "NUEVAVISION\\Compras");

            Assert.AreEqual(220900, apuntado.Pedido);
            Assert.AreEqual("1", apuntado.Empresa);
            Assert.AreEqual(HOY.AddDays(2), apuntado.FechaRecepcionPuesta);
            Assert.AreEqual(HOY.AddDays(4), apuntado.FechaRegla);
            Assert.IsNull(apuntado.FechaModelo, "Reservada para el modelo");
        }

        [TestMethod]
        public void Registrar_SinTabla_NoApuntaNada()
        {
            var repositorio = A.Fake<IRepositorioPlazosProveedores>();
            A.CallTo(() => repositorio.TablaEnviosExiste()).Returns(false);
            ServicioFechaRecepcionAlEnviar servicio = Servicio(repositorio, true, ConPlazos());

            servicio.Registrar("1", 220900, "65", 0, servicio.Calcular("1", "65", 0), "x");

            A.CallTo(() => repositorio.RegistrarEnvio(A<EnvioPedidoCompra>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Registrar_SiFallaNoLanza()
        {
            var repositorio = A.Fake<IRepositorioPlazosProveedores>();
            A.CallTo(() => repositorio.TablaEnviosExiste()).Returns(true);
            A.CallTo(() => repositorio.RegistrarEnvio(A<EnvioPedidoCompra>._)).Throws(new InvalidOperationException("caída"));
            var errores = new List<Exception>();
            ServicioFechaRecepcionAlEnviar servicio = Servicio(repositorio, true, ConPlazos(), errores);

            servicio.Registrar("1", 220900, "65", 0, servicio.Calcular("1", "65", 0), "x");

            Assert.AreEqual(1, errores.Count);
        }

        [TestMethod]
        public void Interruptor_SoloConUno()
        {
            Assert.IsTrue(ServicioFechaRecepcionAlEnviar.Interpretar(" 1 "));
            Assert.IsFalse(ServicioFechaRecepcionAlEnviar.Interpretar("0"));
            Assert.IsFalse(ServicioFechaRecepcionAlEnviar.Interpretar(null));
        }

        #endregion
    }
}
