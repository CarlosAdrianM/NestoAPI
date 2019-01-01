using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.Comisiones;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace NestoAPI.Tests.Models.Comisiones
{
    [TestClass]
    public class VendedorComisionAnualTest
    {
        const string GENERAL = "General";
        const string UNION_LASER = "Unión Láser";
        const string EVA_VISNU = "Eva Visnú";
        const string OTROS_APARATOS = "Otros Aparatos";

        IServicioComisionesAnuales servicio = A.Fake<IServicioComisionesAnuales>();
        IEtiquetaComision etiquetaGeneral = A.Fake<IEtiquetaComision>();
        IEtiquetaComision etiquetaUnionLaser = A.Fake<IEtiquetaComision>();
        IEtiquetaComision etiquetaEvaVisnu = A.Fake<IEtiquetaComision>();
        IEtiquetaComision etiquetaOtrosAparatos = A.Fake<IEtiquetaComision>();
        IEtiquetaComision etiquetaGeneral2 = A.Fake<IEtiquetaComision>();
        IEtiquetaComision etiquetaUnionLaser2 = A.Fake<IEtiquetaComision>();
        IEtiquetaComision etiquetaEvaVisnu2 = A.Fake<IEtiquetaComision>();
        IEtiquetaComision etiquetaOtrosAparatos2 = A.Fake<IEtiquetaComision>();

        public VendedorComisionAnualTest()
        {
            A.CallTo(()=> etiquetaGeneral.Nombre).Returns("General");
            A.CallTo(()=> etiquetaUnionLaser.Nombre).Returns("Unión Láser");
            A.CallTo(()=> etiquetaEvaVisnu.Nombre).Returns("Eva Visnú");
            A.CallTo(()=> etiquetaOtrosAparatos.Nombre).Returns("Otros Aparatos");
            A.CallTo(()=> servicio.Etiquetas).Returns(new Collection<IEtiquetaComision>
            {
                etiquetaGeneral,
                etiquetaUnionLaser,
                etiquetaEvaVisnu,
                etiquetaOtrosAparatos
            });

            A.CallTo(() => etiquetaGeneral2.Nombre).Returns("General");
            A.CallTo(() => etiquetaUnionLaser2.Nombre).Returns("Unión Láser");
            A.CallTo(() => etiquetaEvaVisnu2.Nombre).Returns("Eva Visnú");
            A.CallTo(() => etiquetaOtrosAparatos2.Nombre).Returns("Otros Aparatos");
            A.CallTo(() => servicio.NuevasEtiquetas).Returns(new Collection<IEtiquetaComision>
            {
                etiquetaGeneral2,
                etiquetaUnionLaser2,
                etiquetaEvaVisnu2,
                etiquetaOtrosAparatos2
            });
            A.CallTo(() => servicio.CalculadorProyecciones).Returns(new CalculadorProyecciones2018());
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiElResumenEstaVacioLaProyeccionEsLaVentaPorDoce()
        {
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "General").Single().LeerVentaMes("NV", 2018, 1, true)).Returns(1000);
            A.CallTo(() => servicio.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0,
                    Hasta = 229000.99M,
                    Tipo = 0,
                    TipoExtra = 0
                },
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

            Assert.AreEqual(12000, vendedorComisionAnual.ResumenMesActual.GeneralProyeccion);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiElResumenEstaVacioPeroNoEstamosEnEneroLaProyeccionEsLaVentaProporcional()
        {
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "General").Single().LeerVentaMes("NV", 2018, 2, true)).Returns(1000);
            A.CallTo(() => servicio.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0,
                    Hasta = 229000.99M,
                    Tipo = 0,
                    TipoExtra = 0
                },
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

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018, 2, true);

            Assert.AreEqual(11000, vendedorComisionAnual.ResumenMesActual.GeneralProyeccion);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiElResumenTieneUnMesLaProyeccionEsLaVentaAcumuladaPorSeis()
        {
            IEtiquetaComision etiqueta = A.Fake<IEtiquetaComision>();
            A.CallTo(() => etiqueta.Nombre).Returns("General");
            Collection<IEtiquetaComision> etiquetas = new Collection<IEtiquetaComision>()
            {
                etiqueta
            };
            ResumenComisionesMes resumen = new ResumenComisionesMes
            {
                Mes = 1,
                Vendedor = "NV",
                Etiquetas = etiquetas
            };
            resumen.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Venta = 500;
            Collection<ResumenComisionesMes> coleccionResumenes = new Collection<ResumenComisionesMes>
            {
                resumen
            };
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "General").Single().LeerVentaMes("NV", 2018, 2, true)).Returns(1000);
            A.CallTo(() => servicio.LeerResumenAnno("NV", 2018)).Returns(coleccionResumenes);
            A.CallTo(() => servicio.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0,
                    Hasta = 229000.99M,
                    Tipo = 0,
                    TipoExtra = 0
                },
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

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018, 2, true);

            Assert.AreEqual(9000, vendedorComisionAnual.ResumenMesActual.GeneralProyeccion);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiElResumenTieneUnMesYNoEsEneroLaProyeccionEsLaParteProporcional()
        {
            IEtiquetaComision etiqueta = A.Fake<IEtiquetaComision>();
            A.CallTo(() => etiqueta.Nombre).Returns("General");
            Collection<IEtiquetaComision> etiquetas = new Collection<IEtiquetaComision>()
            {
                etiqueta
            };
            ResumenComisionesMes resumen = new ResumenComisionesMes
            {
                Anno = 2018,
                Mes = 2,
                Vendedor = "NV",
                Etiquetas = etiquetas
            };
            resumen.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Venta = 500;
            Collection<ResumenComisionesMes> coleccionResumenes = new Collection<ResumenComisionesMes>
            {
                resumen
            };
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "General").Single().LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(1000);
            A.CallTo(() => servicio.LeerResumenAnno("NV", 2018)).Returns(coleccionResumenes);
            A.CallTo(() => servicio.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0,
                    Hasta = 229000.99M,
                    Tipo = 0,
                    TipoExtra = 0
                },
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

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018, DateTime.Today.Month, true);

            Assert.AreEqual(8250, vendedorComisionAnual.ResumenMesActual.GeneralProyeccion);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiLaVentaGeneralEsMenorAlPrimerTramoDelMesComisionaATipoDelPrimerTramo()
        {
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "General").Single().LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(1000);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "Unión Láser").Single().LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(10000);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "Eva Visnú").Single().LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(100);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().LeerVentaMes("NV", 2018, DateTime.Today.Month, true)).Returns(10);
            TramoComision tramoBueno = new TramoComision
            {
                Desde = 0,
                Hasta = 1500,
                Tipo = .1M,
                TipoExtra = .02M
            };
            A.CallTo(() => etiquetaGeneral2.SetTipo(tramoBueno)).Returns(.1M);
            A.CallTo(() => etiquetaUnionLaser2.SetTipo(tramoBueno)).Returns(.12M);
            A.CallTo(() => etiquetaEvaVisnu2.SetTipo(tramoBueno)).Returns(.02M);
            A.CallTo(() => etiquetaOtrosAparatos2.SetTipo(tramoBueno)).Returns(.02M);

            A.CallTo(() => servicio.LeerTramosComisionMes("NV")).Returns(new Collection<TramoComision>
            {
                tramoBueno,
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
                    Desde = 0,
                    Hasta = 229000.99M,
                    Tipo = 0,
                    TipoExtra = 0
                },
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

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018, DateTime.Today.Month, true);

            A.CallTo(() => etiquetaEvaVisnu2.Comision).Returns(Math.Round(etiquetaEvaVisnu2.Venta * etiquetaEvaVisnu2.Tipo, 2));
            A.CallTo(() => etiquetaOtrosAparatos2.Comision).Returns(Math.Round(etiquetaOtrosAparatos2.Venta * etiquetaOtrosAparatos2.Tipo, 2));
            A.CallTo(() => etiquetaUnionLaser2.Comision).Returns(Math.Round(etiquetaUnionLaser2.Venta * etiquetaUnionLaser2.Tipo, 2));
            Assert.AreEqual(.1M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Tipo);
            Assert.AreEqual(.12M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Tipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Tipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Tipo);
            Assert.AreEqual(100, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision);
            Assert.AreEqual(1200, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Comision);
            Assert.AreEqual(2, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Comision);
            Assert.AreEqual(.2M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Comision);
            Assert.AreEqual(500, vendedorComisionAnual.ResumenMesActual.GeneralFaltaParaSalto);
            Assert.AreEqual(0, vendedorComisionAnual.ResumenMesActual.GeneralInicioTramo);
            Assert.AreEqual(229000.99M, vendedorComisionAnual.ResumenMesActual.GeneralFinalTramo);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiLaVentaGeneralEstaEntreElPrimerYElSegundoTramoDelMesComisionaATipoDelSegundoTramo()
        {
            TramoComision primerTramo = new TramoComision
            {
                Desde = 0,
                Hasta = 1500,
                Tipo = .1M,
                TipoExtra = .02M
            };
            TramoComision segundoTramo = new TramoComision
            {
                Desde = 1500.01M,
                Hasta = 12000,
                Tipo = .2M,
                TipoExtra = .04M
            };
            A.CallTo(() => etiquetaGeneral.LeerVentaMes("NV", 2018, 9, true)).Returns(4000);
            A.CallTo(() => etiquetaUnionLaser.LeerVentaMes("NV", 2018, 9, true)).Returns(12000);
            A.CallTo(() => etiquetaEvaVisnu.LeerVentaMes("NV", 2018, 9, true)).Returns(200);
            A.CallTo(() => etiquetaOtrosAparatos.LeerVentaMes("NV", 2018, 9, true)).Returns(50);
            A.CallTo(() => etiquetaGeneral2.SetTipo(segundoTramo)).Returns(.2M);
            A.CallTo(() => etiquetaUnionLaser2.SetTipo(segundoTramo)).Returns(.14M);
            A.CallTo(() => etiquetaEvaVisnu2.SetTipo(segundoTramo)).Returns(.04M);
            A.CallTo(() => etiquetaOtrosAparatos2.SetTipo(segundoTramo)).Returns(.02M);
            A.CallTo(() => servicio.LeerTramosComisionMes("NV")).Returns(new Collection<TramoComision>
            {
                primerTramo,
                segundoTramo
            });
            A.CallTo(() => servicio.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0,
                    Hasta = 229000.99M,
                    Tipo = 0,
                    TipoExtra = 0
                },
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

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018, 9, true);

            A.CallTo(() => etiquetaEvaVisnu2.Comision).Returns(Math.Round(etiquetaEvaVisnu2.Venta * etiquetaEvaVisnu2.Tipo, 2));
            A.CallTo(() => etiquetaOtrosAparatos2.Comision).Returns(Math.Round(etiquetaOtrosAparatos2.Venta * etiquetaOtrosAparatos2.Tipo, 2));
            A.CallTo(() => etiquetaUnionLaser2.Comision).Returns(Math.Round(etiquetaUnionLaser2.Venta * etiquetaUnionLaser2.Tipo, 2));
            Assert.AreEqual(.2M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Tipo);
            Assert.AreEqual(.14M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Tipo);
            Assert.AreEqual(.04M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Tipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Tipo);
            Assert.AreEqual(800, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision);
            Assert.AreEqual(1680, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Comision);
            Assert.AreEqual(8, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Comision);
            Assert.AreEqual(1, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Comision);
            Assert.AreEqual(8000, vendedorComisionAnual.ResumenMesActual.GeneralFaltaParaSalto);
            Assert.AreEqual(0, vendedorComisionAnual.ResumenMesActual.GeneralInicioTramo);
            Assert.AreEqual(229000.99M, vendedorComisionAnual.ResumenMesActual.GeneralFinalTramo);
            Assert.AreEqual(16000, vendedorComisionAnual.ResumenMesActual.GeneralProyeccion);
            Assert.IsFalse(vendedorComisionAnual.ResumenMesActual.GeneralBajaSaltoMesSiguiente);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiLaVentaGeneralEsSuperiorAlMayorTramoDelMesComisionaATipoDeLosTramosAnuales()
        {
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "General").Single().LeerVentaMes("NV", 2018, 1, true)).Returns(12001);
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
            TramoComision tramoBueno = new TramoComision
            {
                Desde = 144000,
                Hasta = decimal.MaxValue,
                Tipo = .03M,
                TipoExtra = .001M
            };
            A.CallTo(() => etiquetaGeneral2.SetTipo(tramoBueno)).Returns(.03M);
            A.CallTo(() => etiquetaUnionLaser2.SetTipo(tramoBueno)).Returns(.101M);
            A.CallTo(() => etiquetaEvaVisnu2.SetTipo(tramoBueno)).Returns(.001M);
            A.CallTo(() => etiquetaOtrosAparatos2.SetTipo(tramoBueno)).Returns(.02M);
            A.CallTo(() => servicio.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0,
                    Hasta = 143999.99M,
                    Tipo = 0M,
                    TipoExtra = 0M
                },
                tramoBueno
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018, 1, true);

            A.CallTo(() => etiquetaEvaVisnu2.Comision).Returns(Math.Round(etiquetaEvaVisnu2.Venta * etiquetaEvaVisnu2.Tipo, 2));
            A.CallTo(() => etiquetaOtrosAparatos2.Comision).Returns(Math.Round(etiquetaOtrosAparatos2.Venta * etiquetaOtrosAparatos2.Tipo, 2));
            A.CallTo(() => etiquetaUnionLaser2.Comision).Returns(Math.Round(etiquetaUnionLaser2.Venta * etiquetaUnionLaser2.Tipo, 2));
            Assert.AreEqual(.03M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Tipo);
            Assert.AreEqual(.101M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Tipo);
            Assert.AreEqual(.001M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Tipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Tipo);
            Assert.AreEqual(360.03M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision);
            Assert.AreEqual(1212, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Comision);
            Assert.AreEqual(.2M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Comision);
            Assert.AreEqual(1, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Comision);
            Assert.AreEqual(decimal.MaxValue, vendedorComisionAnual.ResumenMesActual.GeneralFaltaParaSalto);
            Assert.AreEqual(144000, vendedorComisionAnual.ResumenMesActual.GeneralInicioTramo);
            Assert.AreEqual(decimal.MaxValue, vendedorComisionAnual.ResumenMesActual.GeneralFinalTramo);
            Assert.AreEqual(144012, vendedorComisionAnual.ResumenMesActual.GeneralProyeccion);
            Assert.IsFalse(vendedorComisionAnual.ResumenMesActual.GeneralBajaSaltoMesSiguiente);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiComisionaATipoDeLosTramosAnualesCogeElTipoDelTramoCorrecto()
        {
            A.CallTo(() => etiquetaGeneral.LeerVentaMes("NV", 2018, 1, true)).Returns(20000);
            A.CallTo(() => etiquetaUnionLaser.LeerVentaMes("NV", 2018, 1, true)).Returns(12000);
            A.CallTo(() => etiquetaEvaVisnu.LeerVentaMes("NV", 2018, 1, true)).Returns(200);
            A.CallTo(() => etiquetaOtrosAparatos.LeerVentaMes("NV", 2018, 1, true)).Returns(50);
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
            TramoComision tramoBueno = new TramoComision
            {
                Desde = 230000.01M,
                Hasta = decimal.MaxValue,
                Tipo = .072M,
                TipoExtra = .008M
            };
            A.CallTo(() => etiquetaGeneral2.SetTipo(tramoBueno)).Returns(.072M);
            A.CallTo(() => etiquetaUnionLaser2.SetTipo(tramoBueno)).Returns(.108M);
            A.CallTo(() => etiquetaEvaVisnu2.SetTipo(tramoBueno)).Returns(.008M);
            A.CallTo(() => etiquetaOtrosAparatos2.SetTipo(tramoBueno)).Returns(.02M);
            A.CallTo(() => servicio.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 144000,
                    Hasta = 230000,
                    Tipo = .03M,
                    TipoExtra = .001M
                },
                tramoBueno
            });
            
            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018, 1, true);

            A.CallTo(() => etiquetaEvaVisnu2.Comision).Returns(Math.Round(etiquetaEvaVisnu2.Venta * etiquetaEvaVisnu2.Tipo, 2));
            A.CallTo(() => etiquetaOtrosAparatos2.Comision).Returns(Math.Round(etiquetaOtrosAparatos2.Venta * etiquetaOtrosAparatos2.Tipo, 2));
            A.CallTo(() => etiquetaUnionLaser2.Comision).Returns(Math.Round(etiquetaUnionLaser2.Venta * etiquetaUnionLaser2.Tipo, 2));
            Assert.AreEqual(.072M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Tipo);
            Assert.AreEqual(.108M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Tipo);
            Assert.AreEqual(.008M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Tipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Tipo);
            Assert.AreEqual(1440, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision);
            Assert.AreEqual(1296, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Comision);
            Assert.AreEqual(1.6M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Comision);
            Assert.AreEqual(1, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Comision);
            Assert.AreEqual(decimal.MaxValue, vendedorComisionAnual.ResumenMesActual.GeneralFaltaParaSalto);
            Assert.AreEqual(230000.01M, vendedorComisionAnual.ResumenMesActual.GeneralInicioTramo);
            Assert.AreEqual(decimal.MaxValue, vendedorComisionAnual.ResumenMesActual.GeneralFinalTramo);
            Assert.AreEqual(240000, vendedorComisionAnual.ResumenMesActual.GeneralProyeccion);
            Assert.IsTrue(vendedorComisionAnual.ResumenMesActual.GeneralBajaSaltoMesSiguiente);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiHaCobradoComisionLosMesesAnterioresComisionaPorLaDiferencia()
        {
            IEtiquetaComision etiquetaGeneralResumen = A.Fake<IEtiquetaComision>();
            IEtiquetaComision etiquetaUnionLaserResumen = A.Fake<IEtiquetaComision>();
            IEtiquetaComision etiquetaEvaVisnuResumen = A.Fake<IEtiquetaComision>();
            IEtiquetaComision etiquetaOtrosAparatosResumen = A.Fake<IEtiquetaComision>();
            A.CallTo(() => etiquetaGeneralResumen.Nombre).Returns("General");
            A.CallTo(() => etiquetaUnionLaserResumen.Nombre).Returns("Unión Láser");
            A.CallTo(() => etiquetaEvaVisnuResumen.Nombre).Returns("Eva Visnú");
            A.CallTo(() => etiquetaOtrosAparatosResumen.Nombre).Returns("Otros Aparatos");
            Collection<IEtiquetaComision> etiquetasResumen = new Collection<IEtiquetaComision>
            {
                etiquetaGeneralResumen,
                etiquetaUnionLaserResumen,
                etiquetaEvaVisnuResumen,
                etiquetaOtrosAparatosResumen
            };
            ResumenComisionesMes resumen = new ResumenComisionesMes {
                Anno = 2018,
                Mes = 1,
                Vendedor = "NV",
                Etiquetas = etiquetasResumen
            };
            resumen.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Venta = 19000;
            resumen.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Tipo = .1M;
            resumen.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision = 1900;
            Collection<ResumenComisionesMes> coleccionResumenes = new Collection<ResumenComisionesMes>
            {
                resumen
            };
            A.CallTo(() => servicio.LeerResumenAnno("NV", 2018)).Returns(coleccionResumenes);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "General").Single().LeerVentaMes("NV", 2018, 2, true)).Returns(21000);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "Unión Láser").Single().LeerVentaMes("NV", 2018, 2, true)).Returns(12000);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "Eva Visnú").Single().LeerVentaMes("NV", 2018, 2, true)).Returns(200);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "Otros Aparatos").Single().LeerVentaMes("NV", 2018, 2, true)).Returns(50);
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
            TramoComision tramoBueno = new TramoComision
            {
                Desde = 230000.01M,
                Hasta = decimal.MaxValue,
                Tipo = .2M,
                TipoExtra = .008M
            };
            A.CallTo(() => etiquetaGeneral2.SetTipo(tramoBueno)).Returns(.2M);
            A.CallTo(() => etiquetaUnionLaser2.SetTipo(tramoBueno)).Returns(.108M);
            A.CallTo(() => etiquetaEvaVisnu2.SetTipo(tramoBueno)).Returns(.008M);
            A.CallTo(() => etiquetaOtrosAparatos2.SetTipo(tramoBueno)).Returns(.02M);

            A.CallTo(() => servicio.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 144000,
                    Hasta = 230000,
                    Tipo = .1M,
                    TipoExtra = .001M
                },
                tramoBueno
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018, 2, true);

            A.CallTo(() => etiquetaEvaVisnu2.Comision).Returns(Math.Round(etiquetaEvaVisnu2.Venta * etiquetaEvaVisnu2.Tipo, 2));
            A.CallTo(() => etiquetaOtrosAparatos2.Comision).Returns(Math.Round(etiquetaOtrosAparatos2.Venta * etiquetaOtrosAparatos2.Tipo, 2));
            A.CallTo(() => etiquetaUnionLaser2.Comision).Returns(Math.Round(etiquetaUnionLaser2.Venta * etiquetaUnionLaser2.Tipo, 2));
            Assert.AreEqual(.2M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Tipo);
            Assert.AreEqual(.108M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Tipo);
            Assert.AreEqual(.008M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Tipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Tipo);
            Assert.AreEqual(6100, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision);
            Assert.AreEqual(1296, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Comision);
            Assert.AreEqual(1.6M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Comision);
            Assert.AreEqual(1, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Comision);
            Assert.AreEqual(230000.01M, vendedorComisionAnual.ResumenMesActual.GeneralInicioTramo);
            Assert.AreEqual(decimal.MaxValue, vendedorComisionAnual.ResumenMesActual.GeneralFinalTramo);
            Assert.AreEqual(240000, vendedorComisionAnual.ResumenMesActual.GeneralProyeccion);
            Assert.IsTrue(vendedorComisionAnual.ResumenMesActual.GeneralBajaSaltoMesSiguiente);
        }
        
        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiHaCobradoComisionLosMesesAnterioresYBajaDeSaltoNoHayComisionNegativa()
        {
            IEtiquetaComision etiquetaGeneralResumen = A.Fake<IEtiquetaComision>();
            IEtiquetaComision etiquetaUnionLaserResumen = A.Fake<IEtiquetaComision>();
            IEtiquetaComision etiquetaEvaVisnuResumen = A.Fake<IEtiquetaComision>();
            IEtiquetaComision etiquetaOtrosAparatosResumen = A.Fake<IEtiquetaComision>();
            A.CallTo(() => etiquetaGeneralResumen.Nombre).Returns("General");
            A.CallTo(() => etiquetaUnionLaserResumen.Nombre).Returns("Unión Láser");
            A.CallTo(() => etiquetaEvaVisnuResumen.Nombre).Returns("Eva Visnú");
            A.CallTo(() => etiquetaOtrosAparatosResumen.Nombre).Returns("Otros Aparatos");
            Collection<IEtiquetaComision> etiquetasResumen = new Collection<IEtiquetaComision>
            {
                etiquetaGeneralResumen,
                etiquetaUnionLaserResumen,
                etiquetaEvaVisnuResumen,
                etiquetaOtrosAparatosResumen
            };
            ResumenComisionesMes resumen = new ResumenComisionesMes
            {
                Vendedor = "NV",
                Etiquetas = etiquetasResumen,
                Mes = 1,
                Anno = 2018
            };
            resumen.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Venta = 19500;
            resumen.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Tipo = .2M;
            resumen.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision = 3900;
            Collection<ResumenComisionesMes> coleccionResumenes = new Collection<ResumenComisionesMes>
            {
                resumen
            };
            A.CallTo(() => servicio.LeerResumenAnno("NV", 2018)).Returns(coleccionResumenes);

            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "General").Single().LeerVentaMes("NV", 2018, 2, true)).Returns(13000);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "Unión Láser").Single().LeerVentaMes("NV", 2018, 2, true)).Returns(12000);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "Eva Visnú").Single().LeerVentaMes("NV", 2018, 2, true)).Returns(200);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "Otros Aparatos").Single().LeerVentaMes("NV", 2018, 2, true)).Returns(50);
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
            TramoComision tramoBueno = new TramoComision
            {
                Desde = 144000,
                Hasta = 230000,
                Tipo = .1M,
                TipoExtra = .001M
            };
            A.CallTo(() => etiquetaGeneral2.SetTipo(tramoBueno)).Returns(.1M);
            A.CallTo(() => etiquetaUnionLaser2.SetTipo(tramoBueno)).Returns(.101M);
            A.CallTo(() => etiquetaEvaVisnu2.SetTipo(tramoBueno)).Returns(.001M);
            A.CallTo(() => etiquetaOtrosAparatos2.SetTipo(tramoBueno)).Returns(.02M);

            A.CallTo(() => servicio.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                tramoBueno,
                new TramoComision
                {
                    Desde = 230000.01M,
                    Hasta = decimal.MaxValue,
                    Tipo = .2M,
                    TipoExtra = .008M
                }
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018, 2, true);

            A.CallTo(() => etiquetaEvaVisnu2.Comision).Returns(Math.Round(etiquetaEvaVisnu2.Venta * etiquetaEvaVisnu2.Tipo, 2));
            A.CallTo(() => etiquetaOtrosAparatos2.Comision).Returns(Math.Round(etiquetaOtrosAparatos2.Venta * etiquetaOtrosAparatos2.Tipo, 2));
            A.CallTo(() => etiquetaUnionLaser2.Comision).Returns(Math.Round(etiquetaUnionLaser2.Venta * etiquetaUnionLaser2.Tipo, 2));
            Assert.AreEqual(.1M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Tipo);
            Assert.AreEqual(.101M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Tipo);
            Assert.AreEqual(.001M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Tipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Tipo);
            Assert.AreEqual(0, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision);
            Assert.AreEqual(1212, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Comision);
            Assert.AreEqual(.2M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Comision);
            Assert.AreEqual(1, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Comision);
            Assert.AreEqual(5833.33M, vendedorComisionAnual.ResumenMesActual.GeneralFaltaParaSalto);
            Assert.AreEqual(144000, vendedorComisionAnual.ResumenMesActual.GeneralInicioTramo);
            Assert.AreEqual(230000, vendedorComisionAnual.ResumenMesActual.GeneralFinalTramo);
            Assert.AreEqual(195000, vendedorComisionAnual.ResumenMesActual.GeneralProyeccion);
            Assert.IsFalse(vendedorComisionAnual.ResumenMesActual.GeneralBajaSaltoMesSiguiente);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiElMesYaEstaCerradoNoCalculamoSinoQueLoDevolvemosDirectamente()
        {
            ResumenComisionesMes resumen = new ResumenComisionesMes
            {
                Anno = 2018,
                Mes = 1,
                Vendedor = "NV",
                Etiquetas = servicio.Etiquetas
            }
;
            resumen.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Venta = 19500;
            resumen.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Tipo = .2M;
            resumen.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision = 3900;
            Collection<ResumenComisionesMes> coleccionResumenes = new Collection<ResumenComisionesMes>
            {
                resumen
            };
            A.CallTo(() => servicio.LeerResumenAnno("NV", 2018)).Returns(coleccionResumenes);

            coleccionResumenes.FirstOrDefault().Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Venta = 2000;
            coleccionResumenes.FirstOrDefault().Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Tipo = 0.07M;
            coleccionResumenes.FirstOrDefault().Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Venta = 1500;
            coleccionResumenes.FirstOrDefault().Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Tipo = .15M;
            coleccionResumenes.FirstOrDefault().Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Venta = 300;
            coleccionResumenes.FirstOrDefault().Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Tipo = .03M;

            A.CallTo(() => servicio.LeerResumenAnno("NV", 2018)).Returns(coleccionResumenes);
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
                    Desde = 0,
                    Hasta = 229000.99M,
                    Tipo = 0,
                    TipoExtra = 0
                },
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

            A.CallTo(() => etiquetaEvaVisnu.Comision).Returns(Math.Round(etiquetaEvaVisnu.Venta * etiquetaEvaVisnu.Tipo, 2));
            A.CallTo(() => etiquetaOtrosAparatos.Comision).Returns(Math.Round(etiquetaOtrosAparatos.Venta * etiquetaOtrosAparatos.Tipo, 2));
            A.CallTo(() => etiquetaUnionLaser.Comision).Returns(Math.Round(etiquetaUnionLaser.Venta * etiquetaUnionLaser.Tipo, 2));
            Assert.AreEqual(.2M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Tipo);
            Assert.AreEqual(.07M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Tipo);
            Assert.AreEqual(.15M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Tipo);
            Assert.AreEqual(.03M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Tipo);
            Assert.AreEqual(3900, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision);
            Assert.AreEqual(140, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Comision);
            Assert.AreEqual(225, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Comision);
            Assert.AreEqual(9, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Comision);
            Assert.AreEqual(234000, vendedorComisionAnual.ResumenMesActual.GeneralProyeccion);
            Assert.AreEqual(230000, vendedorComisionAnual.ResumenMesActual.GeneralInicioTramo);
            Assert.AreEqual(240000, vendedorComisionAnual.ResumenMesActual.GeneralFinalTramo);
            Assert.IsTrue(vendedorComisionAnual.ResumenMesActual.GeneralBajaSaltoMesSiguiente);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_LoQueFaltaParaSaltoEnEneroEsLaDiferenciaEntreDoce()
        {
            A.CallTo(() => etiquetaGeneral.LeerVentaMes("NV", 2018, 1, true)).Returns(20000);
            TramoComision tramoBueno = new TramoComision
            {
                Desde = 230000.01M,
                Hasta = 250000,
                Tipo = .072M,
                TipoExtra = .008M
            };
            A.CallTo(() => servicio.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 144000,
                    Hasta = 230000,
                    Tipo = .03M,
                    TipoExtra = .001M
                },
                tramoBueno,
                new TramoComision
                {
                    Desde = 250000.01M,
                    Hasta = decimal.MaxValue,
                    Tipo = .08M,
                    TipoExtra = .011M
                },
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018, 1, true);

            Assert.AreEqual(Math.Round(10000M/12,2), vendedorComisionAnual.ResumenMesActual.GeneralFaltaParaSalto);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_LoQueFaltaParaSaltoSiEmpiezaEnFebreroCalculaEnOnceMeses()
        {
            A.CallTo(() => etiquetaGeneral.LeerVentaMes("NV", 2018, 2, true)).Returns(21818.18M);
            TramoComision tramoBueno = new TramoComision
            {
                Desde = 230000.01M,
                Hasta = 250000,
                Tipo = .072M,
                TipoExtra = .008M
            };
            A.CallTo(() => servicio.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 144000,
                    Hasta = 230000,
                    Tipo = .03M,
                    TipoExtra = .001M
                },
                tramoBueno,
                new TramoComision
                {
                    Desde = 250000.01M,
                    Hasta = decimal.MaxValue,
                    Tipo = .08M,
                    TipoExtra = .011M
                },
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018, 2, true);

            Assert.AreEqual(Math.Round(10000M/11,2), vendedorComisionAnual.ResumenMesActual.GeneralFaltaParaSalto);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_LoQueFaltaParaSaltoSiHayDosMesesCalculaEntreSeis()
        {
            IEtiquetaComision etiqueta = A.Fake<IEtiquetaComision>();
            A.CallTo(() => etiqueta.Nombre).Returns("General");
            Collection<IEtiquetaComision> etiquetas = new Collection<IEtiquetaComision>()
            {
                etiqueta
            };
            ResumenComisionesMes resumenEne = new ResumenComisionesMes
            {
                Mes = 1,
                Vendedor = "NV",
                Etiquetas = etiquetas
            };
            //resumenEne.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Venta = 10000;
            ResumenComisionesMes resumenFeb = new ResumenComisionesMes
            {
                Mes = 2,
                Vendedor = "NV",
                Etiquetas = etiquetas
            };
            ResumenComisionesMes resumenMar = new ResumenComisionesMes
            {
                Mes = 3,
                Vendedor = "NV",
                Etiquetas = etiquetas
            };
            ResumenComisionesMes resumenAbr = new ResumenComisionesMes
            {
                Mes = 4,
                Vendedor = "NV",
                Etiquetas = etiquetas
            };
            resumenEne.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Venta = 10000; //son las mismas para todos los meses: 40000
            Collection<ResumenComisionesMes> coleccionResumenes = new Collection<ResumenComisionesMes>
            {
                resumenEne,
                resumenFeb,
                resumenMar,
                resumenAbr
            };
            //A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "General").Single().LeerVentaMes("NV", 2018, 3, true)).Returns(34000);
            A.CallTo(() => servicio.LeerResumenAnno("NV", 2018)).Returns(coleccionResumenes);

            
            A.CallTo(() => etiquetaGeneral.LeerVentaMes("NV", 2018, 5, true)).Returns(60000);
            TramoComision tramoBueno = new TramoComision
            {
                Desde = 230000.01M,
                Hasta = 250000,
                Tipo = .072M,
                TipoExtra = .008M
            };
            A.CallTo(() => servicio.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 144000,
                    Hasta = 230000,
                    Tipo = .03M,
                    TipoExtra = .001M
                },
                tramoBueno,
                new TramoComision
                {
                    Desde = 250000.01M,
                    Hasta = decimal.MaxValue,
                    Tipo = .08M,
                    TipoExtra = .011M
                },
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018, 5, true);

            Assert.AreEqual(Math.Round(10000M / 12 * 5, 2), vendedorComisionAnual.ResumenMesActual.GeneralFaltaParaSalto);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiEsAgostoComisionaAlTipoQueLleveDirectamente()
        {
            IEtiquetaComision etiquetaGeneralResumen = A.Fake<IEtiquetaComision>();
            IEtiquetaComision etiquetaUnionLaserResumen = A.Fake<IEtiquetaComision>();
            IEtiquetaComision etiquetaEvaVisnuResumen = A.Fake<IEtiquetaComision>();
            IEtiquetaComision etiquetaOtrosAparatosResumen = A.Fake<IEtiquetaComision>();
            A.CallTo(() => etiquetaGeneralResumen.Nombre).Returns("General");
            A.CallTo(() => etiquetaUnionLaserResumen.Nombre).Returns("Unión Láser");
            A.CallTo(() => etiquetaEvaVisnuResumen.Nombre).Returns("Eva Visnú");
            A.CallTo(() => etiquetaOtrosAparatosResumen.Nombre).Returns("Otros Aparatos");
            Collection<IEtiquetaComision> etiquetasResumen = new Collection<IEtiquetaComision>
            {
                etiquetaGeneralResumen,
                etiquetaUnionLaserResumen,
                etiquetaEvaVisnuResumen,
                etiquetaOtrosAparatosResumen
            };
            ResumenComisionesMes resumen = new ResumenComisionesMes
            {
                Vendedor = "NV",
                Mes = 7,
                Etiquetas = etiquetasResumen
            };
            resumen.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Venta = 19000;
            resumen.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Tipo = .2M;
            resumen.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision = 3800;
            Collection<ResumenComisionesMes> coleccionResumenes = new Collection<ResumenComisionesMes>
            {
                resumen
            };
            A.CallTo(() => servicio.LeerResumenAnno("NV", 2018)).Returns(coleccionResumenes);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "General").Single().LeerVentaMes("NV", 2018, 8, true)).Returns(1400);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "Unión Láser").Single().LeerVentaMes("NV", 2018, 8, true)).Returns(12000);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "Eva Visnú").Single().LeerVentaMes("NV", 2018, 8, true)).Returns(200);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "Otros Aparatos").Single().LeerVentaMes("NV", 2018, 8, true)).Returns(50);
            TramoComision tramoBuenoMes = new TramoComision
            {
                Desde = 0,
                Hasta = 1500,
                Tipo = .005M,
                TipoExtra = .02M
            };
            A.CallTo(() => servicio.LeerTramosComisionMes("NV")).Returns(new Collection<TramoComision>
            {
                tramoBuenoMes,
                new TramoComision
                {
                    Desde = 1500.01M,
                    Hasta = 10000,
                    Tipo = .02M,
                    TipoExtra = .04M
                }
            });
            TramoComision tramoBuenoAnno = new TramoComision
            {
                Desde = 60000,
                Hasta = 220000,
                Tipo = .1M,
                TipoExtra = .001M
            };
            A.CallTo(() => etiquetaGeneral2.SetTipo(tramoBuenoAnno)).Returns(.1M);
            A.CallTo(() => etiquetaUnionLaser2.SetTipo(tramoBuenoAnno)).Returns(.101M);
            A.CallTo(() => etiquetaEvaVisnu2.SetTipo(tramoBuenoAnno)).Returns(.001M);
            A.CallTo(() => etiquetaOtrosAparatos2.SetTipo(tramoBuenoAnno)).Returns(.02M);
            A.CallTo(() => etiquetaGeneral2.SetTipo(tramoBuenoMes)).Returns(.005M);
            A.CallTo(() => etiquetaUnionLaser2.SetTipo(tramoBuenoMes)).Returns(.12M);
            A.CallTo(() => etiquetaEvaVisnu2.SetTipo(tramoBuenoMes)).Returns(.02M);
            A.CallTo(() => etiquetaOtrosAparatos2.SetTipo(tramoBuenoMes)).Returns(.02M);

            A.CallTo(() => servicio.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                tramoBuenoAnno,
                new TramoComision
                {
                    Desde = 220000.01M,
                    Hasta = int.MaxValue,
                    Tipo = .2M,
                    TipoExtra = .008M
                }
        });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018, 8, true);

            A.CallTo(() => etiquetaEvaVisnu2.Comision).Returns(Math.Round(etiquetaEvaVisnu2.Venta * etiquetaEvaVisnu2.Tipo, 2));
            A.CallTo(() => etiquetaOtrosAparatos2.Comision).Returns(Math.Round(etiquetaOtrosAparatos2.Venta * etiquetaOtrosAparatos2.Tipo, 2));
            A.CallTo(() => etiquetaUnionLaser2.Comision).Returns(Math.Round(etiquetaUnionLaser2.Venta * etiquetaUnionLaser2.Tipo, 2));
            Assert.AreEqual(.1M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Tipo);
            Assert.AreEqual(.101M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Tipo);
            Assert.AreEqual(.001M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Tipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Tipo);
            Assert.AreEqual(140, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision);
            Assert.AreEqual(1212, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Comision);
            Assert.AreEqual(.2M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Comision);
            Assert.AreEqual(1, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Comision);
            Assert.AreEqual(61200, vendedorComisionAnual.ResumenMesActual.GeneralProyeccion);
            Assert.IsFalse(vendedorComisionAnual.ResumenMesActual.GeneralBajaSaltoMesSiguiente);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiLaVentaEsIgualAlTramoMaximoMensualEnTodosLosMesesNoBajaDeTramo()
        {
            IEtiquetaComision etiquetaGeneralResumen = A.Fake<IEtiquetaComision>();
            
            A.CallTo(() => etiquetaGeneralResumen.Nombre).Returns("General");
            Collection<IEtiquetaComision> etiquetasResumen = new Collection<IEtiquetaComision>
            {
                etiquetaGeneralResumen
            };
            ResumenComisionesMes resumen = new ResumenComisionesMes
            {
                Vendedor = "NV",
                Anno = 2018,
                Mes = 1,
                Etiquetas = etiquetasResumen
            };
            resumen.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Venta = 10000;
            resumen.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Tipo = .2M;
            resumen.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision = 2000;
            Collection<ResumenComisionesMes> coleccionResumenes = new Collection<ResumenComisionesMes>
            {
                resumen
            };
            A.CallTo(() => servicio.LeerResumenAnno("NV", 2018)).Returns(coleccionResumenes);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "General").Single().LeerVentaMes("NV", 2018, 2, true)).Returns(10000);
            TramoComision tramoBuenoMes = new TramoComision
            {
                Desde = 1500.01M,
                Hasta = 10000,
                Tipo = .02M,
                TipoExtra = .04M
            };
            A.CallTo(() => servicio.LeerTramosComisionMes("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0,
                    Hasta = 1500,
                    Tipo = .005M,
                    TipoExtra = .02M
                },
                tramoBuenoMes                
            });
            TramoComision tramoBuenoAnno = new TramoComision
            {
                Desde = 60000,
                Hasta = 220000,
                Tipo = .1M,
                TipoExtra = .001M
            };
            A.CallTo(() => servicio.NuevasEtiquetas.Where(e => e.Nombre == "General").Single().SetTipo(tramoBuenoAnno)).Returns(.1M);
            A.CallTo(() => servicio.NuevasEtiquetas.Where(e => e.Nombre == "General").Single().SetTipo(tramoBuenoMes)).Returns(.02M);
            
            A.CallTo(() => servicio.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0,
                    Hasta = 59999.99M,
                    Tipo = 0M,
                    TipoExtra = 0M
                },
                tramoBuenoAnno,
                new TramoComision
                {
                    Desde = 220000.01M,
                    Hasta = int.MaxValue,
                    Tipo = .2M,
                    TipoExtra = .008M
                }
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018, 2, true);

            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Tipo);
            Assert.AreEqual(200, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision);
            Assert.AreEqual(120000, vendedorComisionAnual.ResumenMesActual.GeneralProyeccion);
            Assert.IsFalse(vendedorComisionAnual.ResumenMesActual.GeneralBajaSaltoMesSiguiente);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiLaVentaEsElDobleDelTramoMaximoMensualEnDosMesesSiBajaDeTramo()
        {
            IEtiquetaComision etiquetaGeneralResumen = A.Fake<IEtiquetaComision>();

            A.CallTo(() => etiquetaGeneralResumen.Nombre).Returns("General");
            Collection<IEtiquetaComision> etiquetasResumen = new Collection<IEtiquetaComision>
            {
                etiquetaGeneralResumen
            };
            ResumenComisionesMes resumen = new ResumenComisionesMes
            {
                Vendedor = "NV",
                Anno = 2018,
                Mes = 1,
                Etiquetas = etiquetasResumen
            };
            resumen.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Venta = 20000;
            resumen.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Tipo = .1M;
            resumen.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision = 2000;
            Collection<ResumenComisionesMes> coleccionResumenes = new Collection<ResumenComisionesMes>
            {
                resumen
            };
            A.CallTo(() => servicio.LeerResumenAnno("NV", 2018)).Returns(coleccionResumenes);
            A.CallTo(() => servicio.Etiquetas.Where(e => e.Nombre == "General").Single().LeerVentaMes("NV", 2018, 2, true)).Returns(20000);
            TramoComision tramoBuenoMes = new TramoComision
            {
                Desde = 1500.01M,
                Hasta = 10000,
                Tipo = .02M,
                TipoExtra = .04M
            };
            A.CallTo(() => servicio.LeerTramosComisionMes("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0,
                    Hasta = 1500,
                    Tipo = .005M,
                    TipoExtra = .02M
                },
                tramoBuenoMes
            });
            TramoComision tramoBuenoAnno = new TramoComision
            {
                Desde = 220000,
                Hasta = 250000,
                Tipo = .1M,
                TipoExtra = .001M
            };
            A.CallTo(() => servicio.NuevasEtiquetas.Where(e => e.Nombre == "General").Single().SetTipo(tramoBuenoAnno)).Returns(.1M);
            A.CallTo(() => servicio.NuevasEtiquetas.Where(e => e.Nombre == "General").Single().SetTipo(tramoBuenoMes)).Returns(.02M);

            A.CallTo(() => servicio.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0,
                    Hasta = 219999.99M,
                    Tipo = 0M,
                    TipoExtra = 0M
                },
                tramoBuenoAnno,
                new TramoComision
                {
                    Desde = 250000.01M,
                    Hasta = int.MaxValue,
                    Tipo = .2M,
                    TipoExtra = .008M
                }
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(servicio, "NV", 2018, 2, true);

            Assert.AreEqual(.1M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Tipo);
            Assert.AreEqual(2000, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision);
            Assert.AreEqual(240000, vendedorComisionAnual.ResumenMesActual.GeneralProyeccion);
            Assert.IsTrue(vendedorComisionAnual.ResumenMesActual.GeneralBajaSaltoMesSiguiente);
        }
    }
}
