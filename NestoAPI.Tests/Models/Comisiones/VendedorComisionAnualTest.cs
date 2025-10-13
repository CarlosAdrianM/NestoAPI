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
        private const string GENERAL = "General";
        private const string UNION_LASER = "Unión Láser";
        private const string EVA_VISNU = "Eva Visnú";
        private const string OTROS_APARATOS = "Otros Aparatos";

        private readonly IComisionesAnuales comisiones = A.Fake<IComisionesAnuales>();
        private readonly IEtiquetaComisionVentaAcumulada etiquetaGeneral;
        private readonly IEtiquetaComisionVenta etiquetaUnionLaser = A.Fake<IEtiquetaComisionVenta>();
        private readonly IEtiquetaComisionVenta etiquetaEvaVisnu = A.Fake<IEtiquetaComisionVenta>();
        private readonly IEtiquetaComisionVenta etiquetaOtrosAparatos = A.Fake<IEtiquetaComisionVenta>();
        private readonly IEtiquetaComisionVentaAcumulada etiquetaGeneral2;
        private readonly IEtiquetaComisionVenta etiquetaUnionLaser2 = A.Fake<IEtiquetaComisionVenta>();
        private readonly IEtiquetaComisionVenta etiquetaEvaVisnu2 = A.Fake<IEtiquetaComisionVenta>();
        private readonly IEtiquetaComisionVenta etiquetaOtrosAparatos2 = A.Fake<IEtiquetaComisionVenta>();


        public VendedorComisionAnualTest()
        {
            etiquetaGeneral = A.Fake<IEtiquetaComisionVentaAcumulada>();
            etiquetaGeneral2 = A.Fake<IEtiquetaComisionVentaAcumulada>();

            _ = A.CallTo(() => etiquetaGeneral.Nombre).Returns("General");
            _ = A.CallTo(() => etiquetaGeneral.EsComisionAcumulada).Returns(true);
            _ = A.CallTo(() => etiquetaUnionLaser.Nombre).Returns("Unión Láser");
            _ = A.CallTo(() => etiquetaEvaVisnu.Nombre).Returns("Eva Visnú");
            _ = A.CallTo(() => etiquetaOtrosAparatos.Nombre).Returns("Otros Aparatos");

            _ = A.CallTo(() => etiquetaGeneral2.Nombre).Returns("General");
            _ = A.CallTo(() => etiquetaGeneral2.EsComisionAcumulada).Returns(true);
            _ = A.CallTo(() => etiquetaUnionLaser2.Nombre).Returns("Unión Láser");
            _ = A.CallTo(() => etiquetaEvaVisnu2.Nombre).Returns("Eva Visnú");
            _ = A.CallTo(() => etiquetaOtrosAparatos2.Nombre).Returns("Otros Aparatos");

            _ = A.CallTo(() => comisiones.Etiquetas).Returns(new Collection<IEtiquetaComision>
            {
                etiquetaGeneral,
                etiquetaUnionLaser,
                etiquetaEvaVisnu,
                etiquetaOtrosAparatos
            });

            _ = A.CallTo(() => comisiones.NuevasEtiquetas).Returns(new Collection<IEtiquetaComision>
            {
                etiquetaGeneral2,
                etiquetaUnionLaser2,
                etiquetaEvaVisnu2,
                etiquetaOtrosAparatos2
            });
            _ = A.CallTo(() => comisiones.CalculadorProyecciones).Returns(new CalculadorProyecciones2018());
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiElResumenEstaVacioLaProyeccionEsLaVentaPorDoce()
        {
            _ = A.CallTo(() => etiquetaGeneral.LeerVentaMes("NV", 2018, 1, true, A<bool>.Ignored)).Returns(1000);
            _ = A.CallTo(() => comisiones.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision { Desde = 0, Hasta = 229000.99M, Tipo = 0, TipoExtra = 0 },
                new TramoComision { Desde = 230000, Hasta = 240000, Tipo = .1M, TipoExtra = .001M },
                new TramoComision { Desde = 240000.01M, Hasta = decimal.MaxValue, Tipo = .2M, TipoExtra = .008M }
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(comisiones, "NV", 2018, 1, true);

            var etiquetaGeneralResumen = ComisionesHelper.ObtenerEtiquetaAcumulada(vendedorComisionAnual.ResumenMesActual.Etiquetas);
            Assert.AreEqual(12000, etiquetaGeneralResumen.Proyeccion);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiElResumenEstaVacioPeroNoEstamosEnEneroLaProyeccionEsLaVentaProporcional()
        {
            _ = A.CallTo(() => etiquetaGeneral.LeerVentaMes("NV", 2018, 2, true, A<bool>.Ignored)).Returns(1000);
            _ = A.CallTo(() => comisiones.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision { Desde = 0, Hasta = 229000.99M, Tipo = 0, TipoExtra = 0 },
                new TramoComision { Desde = 230000, Hasta = 240000, Tipo = .1M, TipoExtra = .001M },
                new TramoComision { Desde = 240000.01M, Hasta = decimal.MaxValue, Tipo = .2M, TipoExtra = .008M }
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(comisiones, "NV", 2018, 2, true);

            var etiquetaGeneralResumen = ComisionesHelper.ObtenerEtiquetaAcumulada(vendedorComisionAnual.ResumenMesActual.Etiquetas);
            Assert.AreEqual(11000, etiquetaGeneralResumen.Proyeccion);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiElResumenTieneUnMesLaProyeccionEsLaVentaAcumuladaPorSeis()
        {
            var etiqueta = A.Fake<IEtiquetaComisionVentaAcumulada>();
            _ = A.CallTo(() => etiqueta.Nombre).Returns("General");
            _ = A.CallTo(() => etiqueta.EsComisionAcumulada).Returns(true);
            _ = A.CallTo(() => etiqueta.Venta).Returns(500);
            Collection<IEtiquetaComision> etiquetas = new Collection<IEtiquetaComision>() { etiqueta };
            ResumenComisionesMes resumen = new ResumenComisionesMes
            {
                Mes = 1,
                Vendedor = "NV",
                Etiquetas = etiquetas
            };
            Collection<ResumenComisionesMes> coleccionResumenes = new Collection<ResumenComisionesMes> { resumen };
            _ = A.CallTo(() => etiquetaGeneral.LeerVentaMes("NV", 2018, 2, true, A<bool>.Ignored)).Returns(1000);
            _ = A.CallTo(() => comisiones.LeerResumenAnno("NV", 2018, false)).Returns(coleccionResumenes);
            _ = A.CallTo(() => comisiones.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision { Desde = 0, Hasta = 229000.99M, Tipo = 0, TipoExtra = 0 },
                new TramoComision { Desde = 230000, Hasta = 240000, Tipo = .1M, TipoExtra = .001M },
                new TramoComision { Desde = 240000.01M, Hasta = decimal.MaxValue, Tipo = .2M, TipoExtra = .008M }
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(comisiones, "NV", 2018, 2, true);

            var etiquetaGeneralResumen = ComisionesHelper.ObtenerEtiquetaAcumulada(vendedorComisionAnual.ResumenMesActual.Etiquetas);
            Assert.AreEqual(9000, etiquetaGeneralResumen.Proyeccion);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiElResumenNoEstaVacioPeroElMesEsAnteriorAlPrimeroDevuelveVacio()
        {
            var etiqueta = A.Fake<IEtiquetaComisionVentaAcumulada>();
            _ = A.CallTo(() => etiqueta.Nombre).Returns("General");
            _ = A.CallTo(() => etiqueta.EsComisionAcumulada).Returns(true);
            _ = A.CallTo(() => etiqueta.Venta).Returns(50000);
            Collection<IEtiquetaComision> etiquetas = new Collection<IEtiquetaComision>() { etiqueta };
            ResumenComisionesMes resumen = new ResumenComisionesMes
            {
                Mes = 2,
                Vendedor = "NV",
                Etiquetas = etiquetas
            };
            Collection<ResumenComisionesMes> coleccionResumenes = new Collection<ResumenComisionesMes> { resumen };
            _ = A.CallTo(() => etiquetaGeneral.LeerVentaMes("NV", 2018, 3, true, A<bool>.Ignored)).Returns(1000);
            _ = A.CallTo(() => comisiones.LeerResumenAnno("NV", 2018, false)).Returns(coleccionResumenes);
            _ = A.CallTo(() => comisiones.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision { Desde = 0, Hasta = decimal.MaxValue, Tipo = .2M, TipoExtra = .008M }
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(comisiones, "NV", 2018, 1, true);

            var etiquetaGeneralResumen = ComisionesHelper.ObtenerEtiquetaAcumulada(vendedorComisionAnual.ResumenMesActual.Etiquetas);
            Assert.AreEqual(0, etiquetaGeneralResumen.Proyeccion);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiElResumenTieneUnMesYNoEsEneroLaProyeccionEsLaParteProporcional()
        {
            var etiqueta = A.Fake<IEtiquetaComisionVentaAcumulada>();
            _ = A.CallTo(() => etiqueta.Nombre).Returns("General");
            _ = A.CallTo(() => etiqueta.EsComisionAcumulada).Returns(true);
            _ = A.CallTo(() => etiqueta.Venta).Returns(500);
            Collection<IEtiquetaComision> etiquetas = new Collection<IEtiquetaComision>() { etiqueta };
            ResumenComisionesMes resumen = new ResumenComisionesMes
            {
                Anno = 2018,
                Mes = 2,
                Vendedor = "NV",
                Etiquetas = etiquetas
            };
            Collection<ResumenComisionesMes> coleccionResumenes = new Collection<ResumenComisionesMes> { resumen };

            _ = A.CallTo(() => etiquetaGeneral.LeerVentaMes("NV", 2018, 3, true, A<bool>.Ignored)).Returns(1000);
            _ = A.CallTo(() => comisiones.LeerResumenAnno("NV", 2018, false)).Returns(coleccionResumenes);
            _ = A.CallTo(() => comisiones.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision { Desde = 0, Hasta = 229000.99M, Tipo = 0, TipoExtra = 0 },
                new TramoComision { Desde = 230000, Hasta = 240000, Tipo = .1M, TipoExtra = .001M },
                new TramoComision { Desde = 240000.01M, Hasta = decimal.MaxValue, Tipo = .2M, TipoExtra = .008M }
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(comisiones, "NV", 2018, 3, true);

            var etiquetaGeneralResumen = ComisionesHelper.ObtenerEtiquetaAcumulada(vendedorComisionAnual.ResumenMesActual.Etiquetas);
            Assert.AreEqual(8250, etiquetaGeneralResumen.Proyeccion);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiLaVentaGeneralEsMenorAlPrimerTramoDelMesComisionaATipoDelPrimerTramo()
        {
            _ = A.CallTo(() => etiquetaGeneral.LeerVentaMes("NV", 2018, 7, true, A<bool>.Ignored)).Returns(1000);
            _ = A.CallTo(() => etiquetaUnionLaser.LeerVentaMes("NV", 2018, 7, true, A<bool>.Ignored)).Returns(10000);
            _ = A.CallTo(() => etiquetaEvaVisnu.LeerVentaMes("NV", 2018, 7, true, A<bool>.Ignored)).Returns(100);
            _ = A.CallTo(() => etiquetaOtrosAparatos.LeerVentaMes("NV", 2018, 7, true, A<bool>.Ignored)).Returns(10);
            TramoComision tramoBueno = new TramoComision
            {
                Desde = 0,
                Hasta = 1500,
                Tipo = .1M,
                TipoExtra = .02M
            };
            _ = A.CallTo(() => etiquetaGeneral2.SetTipo(tramoBueno)).Returns(.1M);
            _ = A.CallTo(() => etiquetaUnionLaser2.SetTipo(tramoBueno)).Returns(.12M);
            _ = A.CallTo(() => etiquetaEvaVisnu2.SetTipo(tramoBueno)).Returns(.02M);
            _ = A.CallTo(() => etiquetaOtrosAparatos2.SetTipo(tramoBueno)).Returns(.02M);

            _ = A.CallTo(() => comisiones.LeerTramosComisionMes("NV")).Returns(new Collection<TramoComision>
            {
                tramoBueno,
                new TramoComision { Desde = 1500.01M, Hasta = 12000, Tipo = .2M, TipoExtra = .04M }
            });
            _ = A.CallTo(() => comisiones.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision { Desde = 0, Hasta = 229000.99M, Tipo = 0, TipoExtra = 0 },
                new TramoComision { Desde = 230000, Hasta = 240000, Tipo = .1M, TipoExtra = .001M },
                new TramoComision { Desde = 240000.01M, Hasta = decimal.MaxValue, Tipo = .2M, TipoExtra = .008M }
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(comisiones, "NV", 2018, 7, true);

            _ = A.CallTo(() => etiquetaEvaVisnu2.Comision).Returns(Math.Round(etiquetaEvaVisnu2.Venta * etiquetaEvaVisnu2.Tipo, 2));
            _ = A.CallTo(() => etiquetaOtrosAparatos2.Comision).Returns(Math.Round(etiquetaOtrosAparatos2.Venta * etiquetaOtrosAparatos2.Tipo, 2));
            _ = A.CallTo(() => etiquetaUnionLaser2.Comision).Returns(Math.Round(etiquetaUnionLaser2.Venta * etiquetaUnionLaser2.Tipo, 2));

            var etiquetaGeneralResumen = ComisionesHelper.ObtenerEtiquetaAcumulada(vendedorComisionAnual.ResumenMesActual.Etiquetas);
            Assert.AreEqual(.1M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Tipo);
            Assert.AreEqual(.12M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Tipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Tipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Tipo);
            Assert.AreEqual(100, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision);
            Assert.AreEqual(1200, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Comision);
            Assert.AreEqual(2, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Comision);
            Assert.AreEqual(.2M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Comision);
            Assert.AreEqual(500, etiquetaGeneralResumen.FaltaParaSalto);
            Assert.AreEqual(0, etiquetaGeneralResumen.InicioTramo);
            Assert.AreEqual(229000.99M, etiquetaGeneralResumen.FinalTramo);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiLaVentaGeneralEstaEntreElPrimerYElSegundoTramoDelMesComisionaATipoDelSegundoTramo()
        {
            TramoComision primerTramo = new TramoComision { Desde = 0, Hasta = 1500, Tipo = .1M, TipoExtra = .02M };
            TramoComision segundoTramo = new TramoComision { Desde = 1500.01M, Hasta = 12000, Tipo = .2M, TipoExtra = .04M };

            _ = A.CallTo(() => etiquetaGeneral.LeerVentaMes("NV", 2018, 9, true, A<bool>.Ignored)).Returns(4000);
            _ = A.CallTo(() => etiquetaUnionLaser.LeerVentaMes("NV", 2018, 9, true, A<bool>.Ignored)).Returns(12000);
            _ = A.CallTo(() => etiquetaEvaVisnu.LeerVentaMes("NV", 2018, 9, true, A<bool>.Ignored)).Returns(200);
            _ = A.CallTo(() => etiquetaOtrosAparatos.LeerVentaMes("NV", 2018, 9, true, A<bool>.Ignored)).Returns(50);
            _ = A.CallTo(() => etiquetaGeneral2.SetTipo(segundoTramo)).Returns(.2M);
            _ = A.CallTo(() => etiquetaUnionLaser2.SetTipo(segundoTramo)).Returns(.14M);
            _ = A.CallTo(() => etiquetaEvaVisnu2.SetTipo(segundoTramo)).Returns(.04M);
            _ = A.CallTo(() => etiquetaOtrosAparatos2.SetTipo(segundoTramo)).Returns(.02M);
            _ = A.CallTo(() => comisiones.LeerTramosComisionMes("NV")).Returns(new Collection<TramoComision> { primerTramo, segundoTramo });
            _ = A.CallTo(() => comisiones.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision { Desde = 0, Hasta = 229000.99M, Tipo = 0, TipoExtra = 0 },
                new TramoComision { Desde = 230000, Hasta = 240000, Tipo = .1M, TipoExtra = .001M },
                new TramoComision { Desde = 240000.01M, Hasta = decimal.MaxValue, Tipo = .2M, TipoExtra = .008M }
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(comisiones, "NV", 2018, 9, true);

            _ = A.CallTo(() => etiquetaEvaVisnu2.Comision).Returns(Math.Round(etiquetaEvaVisnu2.Venta * etiquetaEvaVisnu2.Tipo, 2));
            _ = A.CallTo(() => etiquetaOtrosAparatos2.Comision).Returns(Math.Round(etiquetaOtrosAparatos2.Venta * etiquetaOtrosAparatos2.Tipo, 2));
            _ = A.CallTo(() => etiquetaUnionLaser2.Comision).Returns(Math.Round(etiquetaUnionLaser2.Venta * etiquetaUnionLaser2.Tipo, 2));

            var etiquetaGeneralResumen = ComisionesHelper.ObtenerEtiquetaAcumulada(vendedorComisionAnual.ResumenMesActual.Etiquetas);
            Assert.AreEqual(.2M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Tipo);
            Assert.AreEqual(.14M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Tipo);
            Assert.AreEqual(.04M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Tipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Tipo);
            Assert.AreEqual(800, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision);
            Assert.AreEqual(1680, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Comision);
            Assert.AreEqual(8, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Comision);
            Assert.AreEqual(1, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Comision);
            Assert.AreEqual(8000, etiquetaGeneralResumen.FaltaParaSalto);
            Assert.AreEqual(0, etiquetaGeneralResumen.InicioTramo);
            Assert.AreEqual(229000.99M, etiquetaGeneralResumen.FinalTramo);
            Assert.AreEqual(16000, etiquetaGeneralResumen.Proyeccion);
            Assert.IsFalse(etiquetaGeneralResumen.BajaSaltoMesSiguiente);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiLaVentaGeneralEsSuperiorAlMayorTramoDelMesComisionaATipoDeLosTramosAnuales()
        {
            _ = A.CallTo(() => etiquetaGeneral.LeerVentaMes("NV", 2018, 1, true, A<bool>.Ignored)).Returns(12001);
            _ = A.CallTo(() => etiquetaUnionLaser.LeerVentaMes("NV", 2018, 1, true, A<bool>.Ignored)).Returns(12000);
            _ = A.CallTo(() => etiquetaEvaVisnu.LeerVentaMes("NV", 2018, 1, true, A<bool>.Ignored)).Returns(200);
            _ = A.CallTo(() => etiquetaOtrosAparatos.LeerVentaMes("NV", 2018, 1, true, A<bool>.Ignored)).Returns(50);
            _ = A.CallTo(() => comisiones.LeerTramosComisionMes("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision { Desde = 0, Hasta = 1500, Tipo = .1M, TipoExtra = .02M },
                new TramoComision { Desde = 1500.01M, Hasta = 12000, Tipo = .2M, TipoExtra = .04M }
            });
            TramoComision tramoBueno = new TramoComision { Desde = 144000, Hasta = decimal.MaxValue, Tipo = .03M, TipoExtra = .001M };
            _ = A.CallTo(() => etiquetaGeneral2.SetTipo(tramoBueno)).Returns(.03M);
            _ = A.CallTo(() => etiquetaUnionLaser2.SetTipo(tramoBueno)).Returns(.101M);
            _ = A.CallTo(() => etiquetaEvaVisnu2.SetTipo(tramoBueno)).Returns(.001M);
            _ = A.CallTo(() => etiquetaOtrosAparatos2.SetTipo(tramoBueno)).Returns(.02M);
            _ = A.CallTo(() => comisiones.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision { Desde = 0, Hasta = 143999.99M, Tipo = 0M, TipoExtra = 0M },
                tramoBueno
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(comisiones, "NV", 2018, 1, true);

            _ = A.CallTo(() => etiquetaEvaVisnu2.Comision).Returns(Math.Round(etiquetaEvaVisnu2.Venta * etiquetaEvaVisnu2.Tipo, 2));
            _ = A.CallTo(() => etiquetaOtrosAparatos2.Comision).Returns(Math.Round(etiquetaOtrosAparatos2.Venta * etiquetaOtrosAparatos2.Tipo, 2));
            _ = A.CallTo(() => etiquetaUnionLaser2.Comision).Returns(Math.Round(etiquetaUnionLaser2.Venta * etiquetaUnionLaser2.Tipo, 2));

            var etiquetaGeneralResumen = ComisionesHelper.ObtenerEtiquetaAcumulada(vendedorComisionAnual.ResumenMesActual.Etiquetas);
            Assert.AreEqual(.03M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Tipo);
            Assert.AreEqual(.101M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Tipo);
            Assert.AreEqual(.001M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Tipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Tipo);
            Assert.AreEqual(360.03M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision);
            Assert.AreEqual(1212, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Comision);
            Assert.AreEqual(.2M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Comision);
            Assert.AreEqual(1, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Comision);
            Assert.AreEqual(decimal.MaxValue, etiquetaGeneralResumen.FaltaParaSalto);
            Assert.AreEqual(144000, etiquetaGeneralResumen.InicioTramo);
            Assert.AreEqual(decimal.MaxValue, etiquetaGeneralResumen.FinalTramo);
            Assert.AreEqual(144012, etiquetaGeneralResumen.Proyeccion);
            Assert.IsFalse(etiquetaGeneralResumen.BajaSaltoMesSiguiente);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiComisionaATipoDeLosTramosAnualesCogeElTipoDelTramoCorrecto()
        {
            _ = A.CallTo(() => etiquetaGeneral.LeerVentaMes("NV", 2018, 1, true, A<bool>.Ignored)).Returns(20000);
            _ = A.CallTo(() => etiquetaUnionLaser.LeerVentaMes("NV", 2018, 1, true, A<bool>.Ignored)).Returns(12000);
            _ = A.CallTo(() => etiquetaEvaVisnu.LeerVentaMes("NV", 2018, 1, true, A<bool>.Ignored)).Returns(200);
            _ = A.CallTo(() => etiquetaOtrosAparatos.LeerVentaMes("NV", 2018, 1, true, A<bool>.Ignored)).Returns(50);
            _ = A.CallTo(() => comisiones.LeerTramosComisionMes("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision { Desde = 0, Hasta = 1500, Tipo = .1M, TipoExtra = .02M },
                new TramoComision { Desde = 1500.01M, Hasta = 12000, Tipo = .2M, TipoExtra = .04M }
            });
            TramoComision tramoBueno = new TramoComision { Desde = 230000.01M, Hasta = decimal.MaxValue, Tipo = .072M, TipoExtra = .008M };
            _ = A.CallTo(() => etiquetaGeneral2.SetTipo(tramoBueno)).Returns(.072M);
            _ = A.CallTo(() => etiquetaUnionLaser2.SetTipo(tramoBueno)).Returns(.108M);
            _ = A.CallTo(() => etiquetaEvaVisnu2.SetTipo(tramoBueno)).Returns(.008M);
            _ = A.CallTo(() => etiquetaOtrosAparatos2.SetTipo(tramoBueno)).Returns(.02M);
            _ = A.CallTo(() => comisiones.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision { Desde = 144000, Hasta = 230000, Tipo = .03M, TipoExtra = .001M },
                tramoBueno
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(comisiones, "NV", 2018, 1, true);

            _ = A.CallTo(() => etiquetaEvaVisnu2.Comision).Returns(Math.Round(etiquetaEvaVisnu2.Venta * etiquetaEvaVisnu2.Tipo, 2));
            _ = A.CallTo(() => etiquetaOtrosAparatos2.Comision).Returns(Math.Round(etiquetaOtrosAparatos2.Venta * etiquetaOtrosAparatos2.Tipo, 2));
            _ = A.CallTo(() => etiquetaUnionLaser2.Comision).Returns(Math.Round(etiquetaUnionLaser2.Venta * etiquetaUnionLaser2.Tipo, 2));

            var etiquetaGeneralResumen = ComisionesHelper.ObtenerEtiquetaAcumulada(vendedorComisionAnual.ResumenMesActual.Etiquetas);
            Assert.AreEqual(.072M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Tipo);
            Assert.AreEqual(.108M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Tipo);
            Assert.AreEqual(.008M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Tipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Tipo);
            Assert.AreEqual(1440, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision);
            Assert.AreEqual(1296, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Comision);
            Assert.AreEqual(1.6M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Comision);
            Assert.AreEqual(1, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Comision);
            Assert.AreEqual(decimal.MaxValue, etiquetaGeneralResumen.FaltaParaSalto);
            Assert.AreEqual(230000.01M, etiquetaGeneralResumen.InicioTramo);
            Assert.AreEqual(decimal.MaxValue, etiquetaGeneralResumen.FinalTramo);
            Assert.AreEqual(240000, etiquetaGeneralResumen.Proyeccion);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiHaCobradoComisionLosMesesAnterioresComisionaPorLaDiferencia()
        {
            var etiquetaGeneralResumen = A.Fake<IEtiquetaComisionVentaAcumulada>();
            _ = A.CallTo(() => etiquetaGeneralResumen.Nombre).Returns("General");
            _ = A.CallTo(() => etiquetaGeneralResumen.EsComisionAcumulada).Returns(true);
            _ = A.CallTo(() => etiquetaGeneralResumen.Venta).Returns(19000);
            _ = A.CallTo(() => etiquetaGeneralResumen.Tipo).Returns(.1M);
            _ = A.CallTo(() => etiquetaGeneralResumen.Comision).Returns(1900);

            IEtiquetaComisionVenta etiquetaUnionLaserResumen = A.Fake<IEtiquetaComisionVenta>();
            IEtiquetaComisionVenta etiquetaEvaVisnuResumen = A.Fake<IEtiquetaComisionVenta>();
            IEtiquetaComisionVenta etiquetaOtrosAparatosResumen = A.Fake<IEtiquetaComisionVenta>();
            _ = A.CallTo(() => etiquetaUnionLaserResumen.Nombre).Returns("Unión Láser");
            _ = A.CallTo(() => etiquetaEvaVisnuResumen.Nombre).Returns("Eva Visnú");
            _ = A.CallTo(() => etiquetaOtrosAparatosResumen.Nombre).Returns("Otros Aparatos");
            Collection<IEtiquetaComision> etiquetasResumen = new Collection<IEtiquetaComision>
            {
                etiquetaGeneralResumen,
                etiquetaUnionLaserResumen,
                etiquetaEvaVisnuResumen,
                etiquetaOtrosAparatosResumen
            };
            ResumenComisionesMes resumen = new ResumenComisionesMes
            {
                Anno = 2018,
                Mes = 1,
                Vendedor = "NV",
                Etiquetas = etiquetasResumen
            };
            Collection<ResumenComisionesMes> coleccionResumenes = new Collection<ResumenComisionesMes> { resumen };
            _ = A.CallTo(() => comisiones.LeerResumenAnno("NV", 2018, false)).Returns(coleccionResumenes);
            _ = A.CallTo(() => etiquetaGeneral.LeerVentaMes("NV", 2018, 2, true, A<bool>.Ignored)).Returns(21000);
            _ = A.CallTo(() => etiquetaUnionLaser.LeerVentaMes("NV", 2018, 2, true, A<bool>.Ignored)).Returns(12000);
            _ = A.CallTo(() => etiquetaEvaVisnu.LeerVentaMes("NV", 2018, 2, true, A<bool>.Ignored)).Returns(200);
            _ = A.CallTo(() => etiquetaOtrosAparatos.LeerVentaMes("NV", 2018, 2, true, A<bool>.Ignored)).Returns(50);
            _ = A.CallTo(() => comisiones.LeerTramosComisionMes("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision { Desde = 0, Hasta = 1500, Tipo = .1M, TipoExtra = .02M },
                new TramoComision { Desde = 1500.01M, Hasta = 12000, Tipo = .2M, TipoExtra = .04M }
            });
            TramoComision tramoBueno = new TramoComision { Desde = 230000.01M, Hasta = decimal.MaxValue, Tipo = .2M, TipoExtra = .008M };
            _ = A.CallTo(() => etiquetaGeneral2.SetTipo(tramoBueno)).Returns(.2M);
            _ = A.CallTo(() => etiquetaUnionLaser2.SetTipo(tramoBueno)).Returns(.108M);
            _ = A.CallTo(() => etiquetaEvaVisnu2.SetTipo(tramoBueno)).Returns(.008M);
            _ = A.CallTo(() => etiquetaOtrosAparatos2.SetTipo(tramoBueno)).Returns(.02M);
            _ = A.CallTo(() => comisiones.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision { Desde = 144000, Hasta = 230000, Tipo = .1M, TipoExtra = .001M },
                tramoBueno
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(comisiones, "NV", 2018, 2, true);

            _ = A.CallTo(() => etiquetaEvaVisnu2.Comision).Returns(Math.Round(etiquetaEvaVisnu2.Venta * etiquetaEvaVisnu2.Tipo, 2));
            _ = A.CallTo(() => etiquetaOtrosAparatos2.Comision).Returns(Math.Round(etiquetaOtrosAparatos2.Venta * etiquetaOtrosAparatos2.Tipo, 2));
            _ = A.CallTo(() => etiquetaUnionLaser2.Comision).Returns(Math.Round(etiquetaUnionLaser2.Venta * etiquetaUnionLaser2.Tipo, 2));

            var etiquetaGeneralResultado = ComisionesHelper.ObtenerEtiquetaAcumulada(vendedorComisionAnual.ResumenMesActual.Etiquetas);
            Assert.AreEqual(.2M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Tipo);
            Assert.AreEqual(.108M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Tipo);
            Assert.AreEqual(.008M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Tipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Tipo);
            Assert.AreEqual(6100, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision);
            Assert.AreEqual(1296, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Comision);
            Assert.AreEqual(1.6M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Comision);
            Assert.AreEqual(1, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Comision);
            Assert.AreEqual(230000.01M, etiquetaGeneralResultado.InicioTramo);
            Assert.AreEqual(decimal.MaxValue, etiquetaGeneralResultado.FinalTramo);
            Assert.AreEqual(240000, etiquetaGeneralResultado.Proyeccion);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiHaCobradoComisionLosMesesAnterioresYBajaDeSaltoNoHayComisionNegativa()
        {
            var etiquetaGeneralResumen = A.Fake<IEtiquetaComisionVentaAcumulada>();
            _ = A.CallTo(() => etiquetaGeneralResumen.Nombre).Returns("General");
            _ = A.CallTo(() => etiquetaGeneralResumen.EsComisionAcumulada).Returns(true);
            _ = A.CallTo(() => etiquetaGeneralResumen.Venta).Returns(19500);
            _ = A.CallTo(() => etiquetaGeneralResumen.Tipo).Returns(.2M);
            _ = A.CallTo(() => etiquetaGeneralResumen.Comision).Returns(3900);

            IEtiquetaComisionVenta etiquetaUnionLaserResumen = A.Fake<IEtiquetaComisionVenta>();
            IEtiquetaComisionVenta etiquetaEvaVisnuResumen = A.Fake<IEtiquetaComisionVenta>();
            IEtiquetaComisionVenta etiquetaOtrosAparatosResumen = A.Fake<IEtiquetaComisionVenta>();
            _ = A.CallTo(() => etiquetaUnionLaserResumen.Nombre).Returns("Unión Láser");
            _ = A.CallTo(() => etiquetaEvaVisnuResumen.Nombre).Returns("Eva Visnú");
            _ = A.CallTo(() => etiquetaOtrosAparatosResumen.Nombre).Returns("Otros Aparatos");
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
            Collection<ResumenComisionesMes> coleccionResumenes = new Collection<ResumenComisionesMes> { resumen };
            _ = A.CallTo(() => comisiones.LeerResumenAnno("NV", 2018, false)).Returns(coleccionResumenes);
            _ = A.CallTo(() => etiquetaGeneral.LeerVentaMes("NV", 2018, 2, true, A<bool>.Ignored)).Returns(13000);
            _ = A.CallTo(() => etiquetaUnionLaser.LeerVentaMes("NV", 2018, 2, true, A<bool>.Ignored)).Returns(12000);
            _ = A.CallTo(() => etiquetaEvaVisnu.LeerVentaMes("NV", 2018, 2, true, A<bool>.Ignored)).Returns(200);
            _ = A.CallTo(() => etiquetaOtrosAparatos.LeerVentaMes("NV", 2018, 2, true, A<bool>.Ignored)).Returns(50);
            _ = A.CallTo(() => comisiones.LeerTramosComisionMes("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision { Desde = 0, Hasta = 1500, Tipo = .1M, TipoExtra = .02M },
                new TramoComision { Desde = 1500.01M, Hasta = 12000, Tipo = .2M, TipoExtra = .04M }
            });
            TramoComision tramoBueno = new TramoComision { Desde = 144000, Hasta = 230000, Tipo = .1M, TipoExtra = .001M };
            _ = A.CallTo(() => etiquetaGeneral2.SetTipo(tramoBueno)).Returns(.1M);
            _ = A.CallTo(() => etiquetaUnionLaser2.SetTipo(tramoBueno)).Returns(.101M);
            _ = A.CallTo(() => etiquetaEvaVisnu2.SetTipo(tramoBueno)).Returns(.001M);
            _ = A.CallTo(() => etiquetaOtrosAparatos2.SetTipo(tramoBueno)).Returns(.02M);
            _ = A.CallTo(() => comisiones.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                tramoBueno,
                new TramoComision { Desde = 230000.01M, Hasta = decimal.MaxValue, Tipo = .2M, TipoExtra = .008M }
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(comisiones, "NV", 2018, 2, true);

            _ = A.CallTo(() => etiquetaEvaVisnu2.Comision).Returns(Math.Round(etiquetaEvaVisnu2.Venta * etiquetaEvaVisnu2.Tipo, 2));
            _ = A.CallTo(() => etiquetaOtrosAparatos2.Comision).Returns(Math.Round(etiquetaOtrosAparatos2.Venta * etiquetaOtrosAparatos2.Tipo, 2));
            _ = A.CallTo(() => etiquetaUnionLaser2.Comision).Returns(Math.Round(etiquetaUnionLaser2.Venta * etiquetaUnionLaser2.Tipo, 2));

            var etiquetaGeneralResultado = ComisionesHelper.ObtenerEtiquetaAcumulada(vendedorComisionAnual.ResumenMesActual.Etiquetas);
            Assert.AreEqual(.1M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Tipo);
            Assert.AreEqual(.101M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Tipo);
            Assert.AreEqual(.001M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Tipo);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Tipo);
            Assert.AreEqual(0, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision);
            Assert.AreEqual(1212, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Comision);
            Assert.AreEqual(.2M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Comision);
            Assert.AreEqual(1, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Comision);
            Assert.AreEqual(5833.33M, etiquetaGeneralResultado.FaltaParaSalto);
            Assert.AreEqual(144000, etiquetaGeneralResultado.InicioTramo);
            Assert.AreEqual(230000, etiquetaGeneralResultado.FinalTramo);
            Assert.AreEqual(195000, etiquetaGeneralResultado.Proyeccion);
            Assert.IsFalse(etiquetaGeneralResultado.BajaSaltoMesSiguiente);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiElMesYaEstaCerradoNoCalculamoSinoQueLoDevolvemosDirectamente()
        {
            var etiquetaGeneralResumen = A.Fake<IEtiquetaComisionVentaAcumulada>();
            _ = A.CallTo(() => etiquetaGeneralResumen.Nombre).Returns("General");
            _ = A.CallTo(() => etiquetaGeneralResumen.EsComisionAcumulada).Returns(true);
            _ = A.CallTo(() => etiquetaGeneralResumen.Venta).Returns(19500);
            _ = A.CallTo(() => etiquetaGeneralResumen.Tipo).Returns(.2M);
            _ = A.CallTo(() => etiquetaGeneralResumen.Comision).Returns(3900);

            ResumenComisionesMes resumen = new ResumenComisionesMes
            {
                Anno = 2018,
                Mes = 1,
                Vendedor = "NV",
                Etiquetas = new Collection<IEtiquetaComision> { etiquetaGeneralResumen, etiquetaUnionLaser, etiquetaEvaVisnu, etiquetaOtrosAparatos }
            };
            _ = A.CallTo(() => etiquetaUnionLaser.Venta).Returns(2000);
            _ = A.CallTo(() => etiquetaUnionLaser.Tipo).Returns(0.07M);
            _ = A.CallTo(() => etiquetaEvaVisnu.Venta).Returns(1500);
            _ = A.CallTo(() => etiquetaEvaVisnu.Tipo).Returns(.15M);
            _ = A.CallTo(() => etiquetaOtrosAparatos.Venta).Returns(300);
            _ = A.CallTo(() => etiquetaOtrosAparatos.Tipo).Returns(.03M);

            Collection<ResumenComisionesMes> coleccionResumenes = new Collection<ResumenComisionesMes> { resumen };
            _ = A.CallTo(() => comisiones.LeerResumenAnno("NV", 2018, false)).Returns(coleccionResumenes);
            _ = A.CallTo(() => etiquetaGeneral.LeerVentaMes("NV", 2018, 1, true, A<bool>.Ignored)).Returns(13000);
            _ = A.CallTo(() => etiquetaUnionLaser.LeerVentaMes("NV", 2018, 1, true, A<bool>.Ignored)).Returns(12000);
            _ = A.CallTo(() => etiquetaEvaVisnu.LeerVentaMes("NV", 2018, 1, true, A<bool>.Ignored)).Returns(200);
            _ = A.CallTo(() => etiquetaOtrosAparatos.LeerVentaMes("NV", 2018, 1, true, A<bool>.Ignored)).Returns(50);
            _ = A.CallTo(() => comisiones.LeerTramosComisionMes("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision { Desde = 0, Hasta = 1500, Tipo = .1M, TipoExtra = .02M },
                new TramoComision { Desde = 1500.01M, Hasta = 12000, Tipo = .2M, TipoExtra = .04M }
            });
            _ = A.CallTo(() => comisiones.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision { Desde = 0, Hasta = 229000.99M, Tipo = 0, TipoExtra = 0 },
                new TramoComision { Desde = 230000, Hasta = 240000, Tipo = .1M, TipoExtra = .001M },
                new TramoComision { Desde = 240000.01M, Hasta = decimal.MaxValue, Tipo = .2M, TipoExtra = .008M }
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(comisiones, "NV", 2018, 1, true);

            _ = A.CallTo(() => etiquetaEvaVisnu.Comision).Returns(Math.Round(etiquetaEvaVisnu.Venta * etiquetaEvaVisnu.Tipo, 2));
            _ = A.CallTo(() => etiquetaOtrosAparatos.Comision).Returns(Math.Round(etiquetaOtrosAparatos.Venta * etiquetaOtrosAparatos.Tipo, 2));
            _ = A.CallTo(() => etiquetaUnionLaser.Comision).Returns(Math.Round(etiquetaUnionLaser.Venta * etiquetaUnionLaser.Tipo, 2));

            var etiquetaGeneralResultado = ComisionesHelper.ObtenerEtiquetaAcumulada(vendedorComisionAnual.ResumenMesActual.Etiquetas);
            Assert.AreEqual(.2M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Tipo);
            Assert.AreEqual(.07M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Tipo);
            Assert.AreEqual(.15M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Tipo);
            Assert.AreEqual(.03M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Tipo);
            Assert.AreEqual(3900, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision);
            Assert.AreEqual(140, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == UNION_LASER).Single().Comision);
            Assert.AreEqual(225, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == EVA_VISNU).Single().Comision);
            Assert.AreEqual(9, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == OTROS_APARATOS).Single().Comision);
            Assert.AreEqual(234000, etiquetaGeneralResultado.Proyeccion);
            Assert.AreEqual(230000, etiquetaGeneralResultado.InicioTramo);
            Assert.AreEqual(240000, etiquetaGeneralResultado.FinalTramo);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_LoQueFaltaParaSaltoEnEneroEsLaDiferenciaEntreDoce()
        {
            _ = A.CallTo(() => etiquetaGeneral.LeerVentaMes("NV", 2018, 1, true, A<bool>.Ignored)).Returns(20000);
            TramoComision tramoBueno = new TramoComision { Desde = 230000.01M, Hasta = 250000, Tipo = .072M, TipoExtra = .008M };
            _ = A.CallTo(() => comisiones.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision { Desde = 144000, Hasta = 230000, Tipo = .03M, TipoExtra = .001M },
                tramoBueno,
                new TramoComision { Desde = 250000.01M, Hasta = decimal.MaxValue, Tipo = .08M, TipoExtra = .011M },
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(comisiones, "NV", 2018, 1, true);

            var etiquetaGeneralResultado = ComisionesHelper.ObtenerEtiquetaAcumulada(vendedorComisionAnual.ResumenMesActual.Etiquetas);
            Assert.AreEqual(Math.Round(10000M / 12, 2), etiquetaGeneralResultado.FaltaParaSalto);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_LoQueFaltaParaSaltoSiEmpiezaEnFebreroCalculaEnOnceMeses()
        {
            _ = A.CallTo(() => etiquetaGeneral.LeerVentaMes("NV", 2018, 2, true, A<bool>.Ignored)).Returns(21818.18M);
            TramoComision tramoBueno = new TramoComision { Desde = 230000.01M, Hasta = 250000, Tipo = .072M, TipoExtra = .008M };
            _ = A.CallTo(() => comisiones.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision { Desde = 144000, Hasta = 230000, Tipo = .03M, TipoExtra = .001M },
                tramoBueno,
                new TramoComision { Desde = 250000.01M, Hasta = decimal.MaxValue, Tipo = .08M, TipoExtra = .011M },
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(comisiones, "NV", 2018, 2, true);

            var etiquetaGeneralResultado = ComisionesHelper.ObtenerEtiquetaAcumulada(vendedorComisionAnual.ResumenMesActual.Etiquetas);
            Assert.AreEqual(Math.Round(10000M / 11, 2), etiquetaGeneralResultado.FaltaParaSalto);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_LoQueFaltaParaSaltoSiHayDosMesesCalculaEntreSeis()
        {
            var etiqueta = A.Fake<IEtiquetaComisionVentaAcumulada>();
            _ = A.CallTo(() => etiqueta.Nombre).Returns("General");
            _ = A.CallTo(() => etiqueta.EsComisionAcumulada).Returns(true);
            _ = A.CallTo(() => etiqueta.Venta).Returns(10000);
            Collection<IEtiquetaComision> etiquetas = new Collection<IEtiquetaComision>() { etiqueta };
            ResumenComisionesMes resumenEne = new ResumenComisionesMes { Mes = 1, Vendedor = "NV", Etiquetas = etiquetas };
            ResumenComisionesMes resumenFeb = new ResumenComisionesMes { Mes = 2, Vendedor = "NV", Etiquetas = etiquetas };
            ResumenComisionesMes resumenMar = new ResumenComisionesMes { Mes = 3, Vendedor = "NV", Etiquetas = etiquetas };
            ResumenComisionesMes resumenAbr = new ResumenComisionesMes { Mes = 4, Vendedor = "NV", Etiquetas = etiquetas };
            Collection<ResumenComisionesMes> coleccionResumenes = new Collection<ResumenComisionesMes>
            {
                resumenEne, resumenFeb, resumenMar, resumenAbr
            };
            _ = A.CallTo(() => comisiones.LeerResumenAnno("NV", 2018, false)).Returns(coleccionResumenes);
            _ = A.CallTo(() => etiquetaGeneral.LeerVentaMes("NV", 2018, 5, true, A<bool>.Ignored)).Returns(60000);
            TramoComision tramoBueno = new TramoComision { Desde = 230000.01M, Hasta = 250000, Tipo = .072M, TipoExtra = .008M };
            _ = A.CallTo(() => comisiones.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision { Desde = 144000, Hasta = 230000, Tipo = .03M, TipoExtra = .001M },
                tramoBueno,
                new TramoComision { Desde = 250000.01M, Hasta = decimal.MaxValue, Tipo = .08M, TipoExtra = .011M },
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(comisiones, "NV", 2018, 5, true);

            var etiquetaGeneralResultado = ComisionesHelper.ObtenerEtiquetaAcumulada(vendedorComisionAnual.ResumenMesActual.Etiquetas);
            Assert.AreEqual(Math.Round(10000M / 12 * 5, 2), etiquetaGeneralResultado.FaltaParaSalto);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiLaVentaEsIgualAlTramoMaximoMensualEnTodosLosMesesNoBajaDeTramo()
        {
            var etiquetaGeneralResumen = A.Fake<IEtiquetaComisionVentaAcumulada>();
            _ = A.CallTo(() => etiquetaGeneralResumen.Nombre).Returns("General");
            _ = A.CallTo(() => etiquetaGeneralResumen.EsComisionAcumulada).Returns(true);
            _ = A.CallTo(() => etiquetaGeneralResumen.Venta).Returns(10000);
            _ = A.CallTo(() => etiquetaGeneralResumen.Tipo).Returns(.2M);
            _ = A.CallTo(() => etiquetaGeneralResumen.Comision).Returns(2000);
            Collection<IEtiquetaComision> etiquetasResumen = new Collection<IEtiquetaComision> { etiquetaGeneralResumen };
            ResumenComisionesMes resumen = new ResumenComisionesMes
            {
                Vendedor = "NV",
                Anno = 2018,
                Mes = 1,
                Etiquetas = etiquetasResumen
            };
            Collection<ResumenComisionesMes> coleccionResumenes = new Collection<ResumenComisionesMes> { resumen };
            _ = A.CallTo(() => comisiones.LeerResumenAnno("NV", 2018, false)).Returns(coleccionResumenes);
            _ = A.CallTo(() => etiquetaGeneral.LeerVentaMes("NV", 2018, 2, true, A<bool>.Ignored)).Returns(10000);
            TramoComision tramoBuenoMes = new TramoComision { Desde = 1500.01M, Hasta = 10000, Tipo = .02M, TipoExtra = .04M };
            _ = A.CallTo(() => comisiones.LeerTramosComisionMes("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision { Desde = 0, Hasta = 1500, Tipo = .005M, TipoExtra = .02M },
                tramoBuenoMes
            });
            TramoComision tramoBuenoAnno = new TramoComision { Desde = 60000, Hasta = 220000, Tipo = .1M, TipoExtra = .001M };
            _ = A.CallTo(() => etiquetaGeneral2.SetTipo(tramoBuenoAnno)).Returns(.1M);
            _ = A.CallTo(() => etiquetaGeneral2.SetTipo(tramoBuenoMes)).Returns(.02M);
            _ = A.CallTo(() => comisiones.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision { Desde = 0, Hasta = 59999.99M, Tipo = 0M, TipoExtra = 0M },
                tramoBuenoAnno,
                new TramoComision { Desde = 220000.01M, Hasta = int.MaxValue, Tipo = .2M, TipoExtra = .008M }
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(comisiones, "NV", 2018, 2, true);

            var etiquetaGeneralResultado = ComisionesHelper.ObtenerEtiquetaAcumulada(vendedorComisionAnual.ResumenMesActual.Etiquetas);
            Assert.AreEqual(.02M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Tipo);
            Assert.AreEqual(200, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision);
            Assert.AreEqual(120000, etiquetaGeneralResultado.Proyeccion);
            Assert.IsFalse(etiquetaGeneralResultado.BajaSaltoMesSiguiente);
        }

        [TestMethod]
        public void VendedorComisionAnual_CrearResumenMesActual_SiLaVentaEsElDobleDelTramoMaximoMensualEnDosMesesSiBajaDeTramo()
        {
            var etiquetaGeneralResumen = A.Fake<IEtiquetaComisionVentaAcumulada>();
            _ = A.CallTo(() => etiquetaGeneralResumen.Nombre).Returns("General");
            _ = A.CallTo(() => etiquetaGeneralResumen.EsComisionAcumulada).Returns(true);
            _ = A.CallTo(() => etiquetaGeneralResumen.Venta).Returns(20000);
            _ = A.CallTo(() => etiquetaGeneralResumen.Tipo).Returns(.1M);
            _ = A.CallTo(() => etiquetaGeneralResumen.Comision).Returns(2000);
            Collection<IEtiquetaComision> etiquetasResumen = new Collection<IEtiquetaComision> { etiquetaGeneralResumen };
            ResumenComisionesMes resumen = new ResumenComisionesMes
            {
                Vendedor = "NV",
                Anno = 2018,
                Mes = 1,
                Etiquetas = etiquetasResumen
            };
            Collection<ResumenComisionesMes> coleccionResumenes = new Collection<ResumenComisionesMes> { resumen };
            _ = A.CallTo(() => comisiones.LeerResumenAnno("NV", 2018, false)).Returns(coleccionResumenes);
            _ = A.CallTo(() => etiquetaGeneral.LeerVentaMes("NV", 2018, 2, true, A<bool>.Ignored)).Returns(20000);
            TramoComision tramoBuenoMes = new TramoComision { Desde = 1500.01M, Hasta = 10000, Tipo = .02M, TipoExtra = .04M };
            _ = A.CallTo(() => comisiones.LeerTramosComisionMes("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision { Desde = 0, Hasta = 1500, Tipo = .005M, TipoExtra = .02M },
                tramoBuenoMes
            });
            TramoComision tramoBuenoAnno = new TramoComision { Desde = 220000, Hasta = 250000, Tipo = .1M, TipoExtra = .001M };
            _ = A.CallTo(() => etiquetaGeneral2.SetTipo(tramoBuenoAnno)).Returns(.1M);
            _ = A.CallTo(() => etiquetaGeneral2.SetTipo(tramoBuenoMes)).Returns(.02M);
            _ = A.CallTo(() => comisiones.LeerTramosComisionAnno("NV")).Returns(new Collection<TramoComision>
            {
                new TramoComision { Desde = 0, Hasta = 219999.99M, Tipo = 0M, TipoExtra = 0M },
                tramoBuenoAnno,
                new TramoComision { Desde = 250000.01M, Hasta = int.MaxValue, Tipo = .2M, TipoExtra = .008M }
            });

            VendedorComisionAnual vendedorComisionAnual = new VendedorComisionAnual(comisiones, "NV", 2018, 2, true);

            var etiquetaGeneralResultado = ComisionesHelper.ObtenerEtiquetaAcumulada(vendedorComisionAnual.ResumenMesActual.Etiquetas);
            Assert.AreEqual(.1M, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Tipo);
            Assert.AreEqual(2000, vendedorComisionAnual.ResumenMesActual.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision);
            Assert.AreEqual(240000, etiquetaGeneralResultado.Proyeccion);
        }
    }
}