using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.Comisiones;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace NestoAPI.Tests.Models.Comisiones
{

    [TestClass]
    public class VendedorComisionAnualTest
    {
        IServicioComisionesAnuales servicio = A.Fake<IServicioComisionesAnuales>();
        IEtiquetaComision etiquetaGeneral = A.Fake<IEtiquetaComision>();
        IEtiquetaComision etiquetaUnionLaser = A.Fake<IEtiquetaComision>();
        IEtiquetaComision etiquetaEvaVisnu = A.Fake<IEtiquetaComision>();
        IEtiquetaComision etiquetaOtrosAparatos = A.Fake<IEtiquetaComision>();
        
        public VendedorComisionAnualTest()
        {
            A.CallTo(()=>etiquetaGeneral.Nombre).Returns("General");
            A.CallTo(()=>etiquetaUnionLaser.Nombre).Returns("Unión Láser");
            A.CallTo(()=> etiquetaEvaVisnu.Nombre).Returns("Eva Visnú");
            A.CallTo(()=> etiquetaOtrosAparatos.Nombre).Returns("Otros Aparatos");
            A.CallTo(()=> servicio.Etiquetas).Returns(new Collection<IEtiquetaComision>
            {
                etiquetaGeneral,
                etiquetaUnionLaser,
                etiquetaEvaVisnu,
                etiquetaOtrosAparatos
            });
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiElResumenEstaVacioLaProyeccionEsLaVentaPorDoce()
        {
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "General").Single().LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(1000);

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018, DateTime.Today.Month, true);

            Assert.AreEqual(12000, vendedorComisionAnual.ResumenMesActual.GeneralProyeccion);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiElResumenTieneUnMesLaProyeccionEsLaVentaAcumuladaPorSeis()
        {
            A.CallTo(() => servicio.LeerResumenAnno("NV", 2018)).Returns(new Collection<ResumenComisionesMes>
            {
                new ResumenComisionesMes
                {
                    GeneralVenta = 500
                }
            });
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "General").Single().LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(1000);

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018, DateTime.Today.Month, true);

            Assert.AreEqual(9000, vendedorComisionAnual.ResumenMesActual.GeneralProyeccion);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiLaVentaGeneralEsMenorAlPrimerTramoDelMesComisionaATipoDelPrimerTramo()
        {
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "General").Single().LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(1000);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "Unión Láser").Single().LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(10000);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "Eva Visnú").Single().LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(100);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "Otros Aparatos").Single().LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(10);
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

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018, DateTime.Today.Month, true);

            Assert.AreEqual(.1M, vendedorComisionAnual.ResumenMesActual.GeneralTipo);
            Assert.AreEqual(.12M, vendedorComisionAnual.ResumenMesActual.UnionLaserTipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.EvaVisnuTipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.OtrosAparatosTipo);
            Assert.AreEqual(100, vendedorComisionAnual.ResumenMesActual.GeneralComision);
            Assert.AreEqual(1200, vendedorComisionAnual.ResumenMesActual.UnionLaserComision);
            Assert.AreEqual(2, vendedorComisionAnual.ResumenMesActual.EvaVisnuComision);
            Assert.AreEqual(.2M, vendedorComisionAnual.ResumenMesActual.OtrosAparatosComision);
            Assert.AreEqual(500, vendedorComisionAnual.ResumenMesActual.GeneralFaltaParaSalto);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiLaVentaGeneralEstaEntreElPrimerYElSegundoTramoDelMesComisionaATipoDelSegundoTramo()
        {
            A.CallTo(() => etiquetaGeneral.LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(4000);
            A.CallTo(() => etiquetaUnionLaser.LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(12000);
            A.CallTo(() => etiquetaEvaVisnu.LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(200);
            A.CallTo(() => etiquetaOtrosAparatos.LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(50);
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

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018, DateTime.Today.Month, true);

            Assert.AreEqual(.2M, vendedorComisionAnual.ResumenMesActual.GeneralTipo);
            Assert.AreEqual(.14M, vendedorComisionAnual.ResumenMesActual.UnionLaserTipo);
            Assert.AreEqual(.04M, vendedorComisionAnual.ResumenMesActual.EvaVisnuTipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.OtrosAparatosTipo);
            Assert.AreEqual(800, vendedorComisionAnual.ResumenMesActual.GeneralComision);
            Assert.AreEqual(1680, vendedorComisionAnual.ResumenMesActual.UnionLaserComision);
            Assert.AreEqual(8, vendedorComisionAnual.ResumenMesActual.EvaVisnuComision);
            Assert.AreEqual(1, vendedorComisionAnual.ResumenMesActual.OtrosAparatosComision);
            Assert.AreEqual(8000, vendedorComisionAnual.ResumenMesActual.GeneralFaltaParaSalto);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiLaVentaGeneralEsSuperiorAlMayorTramoDelMesComisionaATipoDeLosTramosAnuales()
        {
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "General").Single().LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(12001);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "Unión Láser").Single().LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(12000);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "Eva Visnú").Single().LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(200);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "Otros Aparatos").Single().LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(50);
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
                    Hasta = decimal.MaxValue,
                    Tipo = .03M,
                    TipoExtra = .001M
                }
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018,DateTime.Today.Month, true);

            Assert.AreEqual(.03M, vendedorComisionAnual.ResumenMesActual.GeneralTipo);
            Assert.AreEqual(.101M, vendedorComisionAnual.ResumenMesActual.UnionLaserTipo);
            Assert.AreEqual(.001M, vendedorComisionAnual.ResumenMesActual.EvaVisnuTipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.OtrosAparatosTipo);
            Assert.AreEqual(360.03M, vendedorComisionAnual.ResumenMesActual.GeneralComision);
            Assert.AreEqual(1212, vendedorComisionAnual.ResumenMesActual.UnionLaserComision);
            Assert.AreEqual(.2M, vendedorComisionAnual.ResumenMesActual.EvaVisnuComision);
            Assert.AreEqual(1, vendedorComisionAnual.ResumenMesActual.OtrosAparatosComision);
            Assert.AreEqual(decimal.MaxValue, vendedorComisionAnual.ResumenMesActual.GeneralFaltaParaSalto);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiComisionaATipoDeLosTramosAnualesCogeElTipoDelTramoCorrecto()
        {
            A.CallTo(() => etiquetaGeneral.LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(20000);
            A.CallTo(() => etiquetaUnionLaser.LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(12000);
            A.CallTo(() => etiquetaEvaVisnu.LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(200);
            A.CallTo(() => etiquetaOtrosAparatos.LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(50);
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
                    Hasta = decimal.MaxValue,
                    Tipo = .072M,
                    TipoExtra = .008M
                }
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018, DateTime.Today.Month, true);

            Assert.AreEqual(.072M, vendedorComisionAnual.ResumenMesActual.GeneralTipo);
            Assert.AreEqual(.108M, vendedorComisionAnual.ResumenMesActual.UnionLaserTipo);
            Assert.AreEqual(.008M, vendedorComisionAnual.ResumenMesActual.EvaVisnuTipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.OtrosAparatosTipo);
            Assert.AreEqual(1440, vendedorComisionAnual.ResumenMesActual.GeneralComision);
            Assert.AreEqual(1296, vendedorComisionAnual.ResumenMesActual.UnionLaserComision);
            Assert.AreEqual(1.6M, vendedorComisionAnual.ResumenMesActual.EvaVisnuComision);
            Assert.AreEqual(1, vendedorComisionAnual.ResumenMesActual.OtrosAparatosComision);
            Assert.AreEqual(decimal.MaxValue, vendedorComisionAnual.ResumenMesActual.GeneralFaltaParaSalto);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiHaCobradoComisionLosMesesAnterioresComisionaPorLaDiferencia()
        {
            A.CallTo(() => servicio.LeerResumenAnno("NV", 2018)).Returns(new Collection<ResumenComisionesMes>
            {
                new ResumenComisionesMes
                {
                    GeneralVenta = 19000,
                    GeneralTipo = .1M,
                    GeneralComision = 1900
                }
            });
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "General").Single().LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(21000);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "Unión Láser").Single().LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(12000);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "Eva Visnú").Single().LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(200);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "Otros Aparatos").Single().LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(50);
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

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018, DateTime.Today.Month, true);

            Assert.AreEqual(.2M, vendedorComisionAnual.ResumenMesActual.GeneralTipo);
            Assert.AreEqual(.108M, vendedorComisionAnual.ResumenMesActual.UnionLaserTipo);
            Assert.AreEqual(.008M, vendedorComisionAnual.ResumenMesActual.EvaVisnuTipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.OtrosAparatosTipo);
            Assert.AreEqual(6100, vendedorComisionAnual.ResumenMesActual.GeneralComision);
            Assert.AreEqual(1296, vendedorComisionAnual.ResumenMesActual.UnionLaserComision);
            Assert.AreEqual(1.6M, vendedorComisionAnual.ResumenMesActual.EvaVisnuComision);
            Assert.AreEqual(1, vendedorComisionAnual.ResumenMesActual.OtrosAparatosComision);
        }
        
        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiHaCobradoComisionLosMesesAnterioresYBajaDeSaltoNoHayComisionNegativa()
        {
            A.CallTo(() => servicio.LeerResumenAnno("NV", 2018)).Returns(new Collection<ResumenComisionesMes>
            {
                new ResumenComisionesMes
                {
                    GeneralVenta = 19500,
                    GeneralTipo = .2M,
                    GeneralComision = 3900
                }
            });
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "General").Single().LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(13000);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "Unión Láser").Single().LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(12000);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "Eva Visnú").Single().LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(200);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "Otros Aparatos").Single().LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(50);
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
                    Hasta = decimal.MaxValue,
                    Tipo = .2M,
                    TipoExtra = .008M
                }
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018, DateTime.Today.Month, true);

            Assert.AreEqual(.1M, vendedorComisionAnual.ResumenMesActual.GeneralTipo);
            Assert.AreEqual(.101M, vendedorComisionAnual.ResumenMesActual.UnionLaserTipo);
            Assert.AreEqual(.001M, vendedorComisionAnual.ResumenMesActual.EvaVisnuTipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.OtrosAparatosTipo);
            Assert.AreEqual(0, vendedorComisionAnual.ResumenMesActual.GeneralComision);
            Assert.AreEqual(1212, vendedorComisionAnual.ResumenMesActual.UnionLaserComision);
            Assert.AreEqual(.2M, vendedorComisionAnual.ResumenMesActual.EvaVisnuComision);
            Assert.AreEqual(1, vendedorComisionAnual.ResumenMesActual.OtrosAparatosComision);
            Assert.AreEqual(5833.33M, vendedorComisionAnual.ResumenMesActual.GeneralFaltaParaSalto);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiElMesYaEstaCerradoNoCalculamoSinoQueLoDevolvemosDirectamente()
        {
            A.CallTo(() => servicio.LeerResumenAnno("NV", 2018)).Returns(new Collection<ResumenComisionesMes>
            {
                new ResumenComisionesMes
                {
                    Anno = 2018,
                    Mes = 1,
                    GeneralVenta = 19500,
                    GeneralTipo = .2M,
                    GeneralComision = 3900,
                    UnionLaserVenta = 2000,
                    UnionLaserTipo = 0.07M,
                    EvaVisnuVenta = 1500,
                    EvaVisnuTipo = .15M,
                    OtrosAparatosVenta = 300,
                    OtrosAparatosTipo = .03M
                }
            });
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "General").Single().LeerVentaMes("NV", 2018, 1, true)).Returns(13000);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "Unión Láser").Single().LeerVentaMes("NV", 2018, 1, true)).Returns(12000);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "Eva Visnú").Single().LeerVentaMes("NV", 2018, 1, true)).Returns(200);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "Otros Aparatos").Single().LeerVentaMes("NV", 2018, 1, true)).Returns(50);
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
                    Desde = 230000,
                    Hasta = 240000,
                    Tipo = .1M,
                    TipoExtra = .001M
                },
                new TramoComision
                {
                    Desde = 240000.01M,
                    Hasta = decimal.MaxValue,
                    Tipo = .2M,
                    TipoExtra = .008M
                }
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018, 1, true);

            Assert.AreEqual(.2M, vendedorComisionAnual.ResumenMesActual.GeneralTipo);
            Assert.AreEqual(.07M, vendedorComisionAnual.ResumenMesActual.UnionLaserTipo);
            Assert.AreEqual(.15M, vendedorComisionAnual.ResumenMesActual.EvaVisnuTipo);
            Assert.AreEqual(.03M, vendedorComisionAnual.ResumenMesActual.OtrosAparatosTipo);
            Assert.AreEqual(3900, vendedorComisionAnual.ResumenMesActual.GeneralComision);
            Assert.AreEqual(140, vendedorComisionAnual.ResumenMesActual.UnionLaserComision);
            Assert.AreEqual(225, vendedorComisionAnual.ResumenMesActual.EvaVisnuComision);
            Assert.AreEqual(9, vendedorComisionAnual.ResumenMesActual.OtrosAparatosComision);
            Assert.AreEqual(234000, vendedorComisionAnual.ResumenMesActual.GeneralProyeccion);
        }
    }

    // si el mes es agosto, se pagan al tipo que lleve a ese momento
}
