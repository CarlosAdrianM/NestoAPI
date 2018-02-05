using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.Comisiones;
using System;
using System.Collections.ObjectModel;

namespace NestoAPI.Tests.Models.Comisiones
{
    [TestClass]
    public class VendedorComisionAnualTest
    {
        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiElResumenEstaVacioLaProyeccionEsLaVentaPorDoce()
        {
            IServicioComisionesAnuales servicio = A.Fake<IServicioComisionesAnuales>();
            A.CallTo(() => servicio.LeerGeneralVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(1000);

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018);

            Assert.AreEqual(12000, vendedorComisionAnual.ResumenMesActual.GeneralProyeccion);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiElResumenTieneUnMesLaProyeccionEsLaVentaAcumuladaPorSeis()
        {
            IServicioComisionesAnuales servicio = A.Fake<IServicioComisionesAnuales>();
            A.CallTo(() => servicio.LeerResumenAnno("NV", 2018)).Returns(new Collection<ResumenComisionesMes>
            {
                new ResumenComisionesMes
                {
                    GeneralVenta = 500
                }
            });
            A.CallTo(() => servicio.LeerGeneralVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(1000);

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018);

            Assert.AreEqual(9000, vendedorComisionAnual.ResumenMesActual.GeneralProyeccion);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiLaVentaGeneralEsMenorAlPrimerTramoDelMesComisionaATipoDelPrimerTramo()
        {
            IServicioComisionesAnuales servicio = A.Fake<IServicioComisionesAnuales>();
            A.CallTo(() => servicio.LeerGeneralVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(1000);
            A.CallTo(() => servicio.LeerUnionLaserVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(10000);
            A.CallTo(() => servicio.LeerEvaVisnuVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(100);
            A.CallTo(() => servicio.LeerOtrosAparatosVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(10);
            A.CallTo(() => servicio.LeerTramosComisionMes("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0,
                    Hasta = 1500,
                    Tipo = .1M,
                    TipoExtra = .02M
                },
                new TramoComision
                {
                    Desde = 1500.01M,
                    Hasta = 12000,
                    Tipo = .2M,
                    TipoExtra = .04M
                }
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018);

            Assert.AreEqual(.1M, vendedorComisionAnual.ResumenMesActual.GeneralTipo);
            Assert.AreEqual(.12M, vendedorComisionAnual.ResumenMesActual.UnionLaserTipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.EvaVisnuTipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.OtrosAparatosTipo);
            Assert.AreEqual(100, vendedorComisionAnual.ResumenMesActual.GeneralComision);
            Assert.AreEqual(1200, vendedorComisionAnual.ResumenMesActual.UnionLaserComision);
            Assert.AreEqual(2, vendedorComisionAnual.ResumenMesActual.EvaVisnuComision);
            Assert.AreEqual(.2M, vendedorComisionAnual.ResumenMesActual.OtrosAparatosComision);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiLaVentaGeneralEstaEntreElPrimerYElSegundoTramoDelMesComisionaATipoDelSegundoTramo()
        {
            IServicioComisionesAnuales servicio = A.Fake<IServicioComisionesAnuales>();
            A.CallTo(() => servicio.LeerGeneralVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(4000);
            A.CallTo(() => servicio.LeerUnionLaserVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(12000);
            A.CallTo(() => servicio.LeerEvaVisnuVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(200);
            A.CallTo(() => servicio.LeerOtrosAparatosVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(50);
            A.CallTo(() => servicio.LeerTramosComisionMes("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0,
                    Hasta = 1500,
                    Tipo = .1M,
                    TipoExtra = .02M
                },
                new TramoComision
                {
                    Desde = 1500.01M,
                    Hasta = 12000,
                    Tipo = .2M,
                    TipoExtra = .04M
                }
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018);

            Assert.AreEqual(.2M, vendedorComisionAnual.ResumenMesActual.GeneralTipo);
            Assert.AreEqual(.14M, vendedorComisionAnual.ResumenMesActual.UnionLaserTipo);
            Assert.AreEqual(.04M, vendedorComisionAnual.ResumenMesActual.EvaVisnuTipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.OtrosAparatosTipo);
            Assert.AreEqual(800, vendedorComisionAnual.ResumenMesActual.GeneralComision);
            Assert.AreEqual(1680, vendedorComisionAnual.ResumenMesActual.UnionLaserComision);
            Assert.AreEqual(8, vendedorComisionAnual.ResumenMesActual.EvaVisnuComision);
            Assert.AreEqual(1, vendedorComisionAnual.ResumenMesActual.OtrosAparatosComision);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiLaVentaGeneralEsSuperiorAlMayorTramoDelMesComisionaATipoDeLosTramosAnuales()
        {
            IServicioComisionesAnuales servicio = A.Fake<IServicioComisionesAnuales>();
            A.CallTo(() => servicio.LeerGeneralVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(12001);
            A.CallTo(() => servicio.LeerUnionLaserVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(12000);
            A.CallTo(() => servicio.LeerEvaVisnuVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(200);
            A.CallTo(() => servicio.LeerOtrosAparatosVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(50);
            A.CallTo(() => servicio.LeerTramosComisionMes("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0,
                    Hasta = 1500,
                    Tipo = .1M,
                    TipoExtra = .02M
                },
                new TramoComision
                {
                    Desde = 1500.01M,
                    Hasta = 12000,
                    Tipo = .2M,
                    TipoExtra = .04M
                }
            });
            A.CallTo(() => servicio.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 144000,
                    Hasta = int.MaxValue,
                    Tipo = .03M,
                    TipoExtra = .001M
                }
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018);

            Assert.AreEqual(.03M, vendedorComisionAnual.ResumenMesActual.GeneralTipo);
            Assert.AreEqual(.101M, vendedorComisionAnual.ResumenMesActual.UnionLaserTipo);
            Assert.AreEqual(.001M, vendedorComisionAnual.ResumenMesActual.EvaVisnuTipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.OtrosAparatosTipo);
            Assert.AreEqual(360.03M, vendedorComisionAnual.ResumenMesActual.GeneralComision);
            Assert.AreEqual(1212, vendedorComisionAnual.ResumenMesActual.UnionLaserComision);
            Assert.AreEqual(.2M, vendedorComisionAnual.ResumenMesActual.EvaVisnuComision);
            Assert.AreEqual(1, vendedorComisionAnual.ResumenMesActual.OtrosAparatosComision);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiComisionaATipoDeLosTramosAnualesCogeElTipoDelTramoCorrecto()
        {
            IServicioComisionesAnuales servicio = A.Fake<IServicioComisionesAnuales>();
            A.CallTo(() => servicio.LeerGeneralVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(20000);
            A.CallTo(() => servicio.LeerUnionLaserVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(12000);
            A.CallTo(() => servicio.LeerEvaVisnuVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(200);
            A.CallTo(() => servicio.LeerOtrosAparatosVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(50);
            A.CallTo(() => servicio.LeerTramosComisionMes("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0,
                    Hasta = 1500,
                    Tipo = .1M,
                    TipoExtra = .02M
                },
                new TramoComision
                {
                    Desde = 1500.01M,
                    Hasta = 12000,
                    Tipo = .2M,
                    TipoExtra = .04M
                }
            });
            A.CallTo(() => servicio.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 144000,
                    Hasta = 230000,
                    Tipo = .03M,
                    TipoExtra = .001M
                },
                new TramoComision
                {
                    Desde = 230000.01M,
                    Hasta = int.MaxValue,
                    Tipo = .072M,
                    TipoExtra = .008M
                }
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018);

            Assert.AreEqual(.072M, vendedorComisionAnual.ResumenMesActual.GeneralTipo);
            Assert.AreEqual(.108M, vendedorComisionAnual.ResumenMesActual.UnionLaserTipo);
            Assert.AreEqual(.008M, vendedorComisionAnual.ResumenMesActual.EvaVisnuTipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.OtrosAparatosTipo);
            Assert.AreEqual(1440, vendedorComisionAnual.ResumenMesActual.GeneralComision);
            Assert.AreEqual(1296, vendedorComisionAnual.ResumenMesActual.UnionLaserComision);
            Assert.AreEqual(1.6M, vendedorComisionAnual.ResumenMesActual.EvaVisnuComision);
            Assert.AreEqual(1, vendedorComisionAnual.ResumenMesActual.OtrosAparatosComision);
        }


        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiHaCobradoComisionLosMesesAnterioresComisionaPorLaDiferencia()
        {
            IServicioComisionesAnuales servicio = A.Fake<IServicioComisionesAnuales>();
            A.CallTo(() => servicio.LeerResumenAnno("NV", 2018)).Returns(new Collection<ResumenComisionesMes>
            {
                new ResumenComisionesMes
                {
                    GeneralVenta = 19000,
                    GeneralTipo = .1M,
                    GeneralComision = 1900
                }
            });
            A.CallTo(() => servicio.LeerGeneralVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(21000);
            A.CallTo(() => servicio.LeerUnionLaserVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(12000);
            A.CallTo(() => servicio.LeerEvaVisnuVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(200);
            A.CallTo(() => servicio.LeerOtrosAparatosVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(50);
            A.CallTo(() => servicio.LeerTramosComisionMes("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0,
                    Hasta = 1500,
                    Tipo = .1M,
                    TipoExtra = .02M
                },
                new TramoComision
                {
                    Desde = 1500.01M,
                    Hasta = 12000,
                    Tipo = .2M,
                    TipoExtra = .04M
                }
            });
            A.CallTo(() => servicio.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 144000,
                    Hasta = 230000,
                    Tipo = .1M,
                    TipoExtra = .001M
                },
                new TramoComision
                {
                    Desde = 230000.01M,
                    Hasta = int.MaxValue,
                    Tipo = .2M,
                    TipoExtra = .008M
                }
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018);

            Assert.AreEqual(.2M, vendedorComisionAnual.ResumenMesActual.GeneralTipo);
            Assert.AreEqual(.108M, vendedorComisionAnual.ResumenMesActual.UnionLaserTipo);
            Assert.AreEqual(.008M, vendedorComisionAnual.ResumenMesActual.EvaVisnuTipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.OtrosAparatosTipo);
            Assert.AreEqual(6100, vendedorComisionAnual.ResumenMesActual.GeneralComision);
            Assert.AreEqual(1296, vendedorComisionAnual.ResumenMesActual.UnionLaserComision);
            Assert.AreEqual(1.6M, vendedorComisionAnual.ResumenMesActual.EvaVisnuComision);
            Assert.AreEqual(1, vendedorComisionAnual.ResumenMesActual.OtrosAparatosComision);
        }
    }

    // si el mes es agosto, se pagan al tipo que lleve a ese momento
}
