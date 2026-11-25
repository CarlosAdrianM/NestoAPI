using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infraestructure.Informes
{
    /// <summary>NestoAPI#893: LA REGLA de la pérdida desconocida por almacén (ALG, REI, ALC), mes a mes y acumulada, a coste.</summary>
    [TestClass]
    public class GestorPerdidaDesconocidaTests
    {
        private static readonly DateTime HASTA = new DateTime(2027, 3, 15);

        private static FilaPerdidaDesconocidaDTO F(string almacen, int mes, string producto, int altas, int bajas,
            string diario = "_RegInvent", decimal precio = 10m) => new FilaPerdidaDesconocidaDTO
            {
                Almacen = almacen + " ",
                Mes = mes,
                Producto = producto + "   ",
                Diario = diario,
                Altas = altas,
                Bajas = bajas,
                Nombre = "NOMBRE " + producto,
                Familia = "Fam",
                PrecioMedio = precio
            };

        private static PeriodoPerdidaDesconocidaDTO Mes(PerdidaDesconocidaDTO i, string almacen, int mes) =>
            mes == 0 ? i.Almacenes.Single(a => a.Almacen == almacen).Acumulado : i.Almacenes.Single(a => a.Almacen == almacen).Meses.Single(m => m.Mes == mes);

        [TestMethod]
        public void SinFilas_TresAlmacenesConSusMesesACero()
        {
            PerdidaDesconocidaDTO i = GestorPerdidaDesconocida.Construir(null, HASTA);

            CollectionAssert.AreEqual(new[] { "ALG", "REI", "ALC" }, i.Almacenes.Select(a => a.Almacen).ToArray());
            Assert.IsTrue(i.Almacenes.All(a => a.Meses.Count == 3 && a.Acumulado.Mes == 0));
            Assert.AreEqual(new DateTime(2027, 1, 1), i.Desde);
            Assert.AreEqual(0, i.Total.FaltaUnidades);
            Assert.AreEqual(3, i.EnTransito.Count);
            Assert.AreEqual(0, i.EnTransitoAcumulado.FaltaUnidades);
        }

        [TestMethod]
        public void FaltaEnUnAlmacenSinNadaEnOtro_EsPerdidaDeVerdad()
        {
            PerdidaDesconocidaDTO i = GestorPerdidaDesconocida.Construir(new[] { F("REI", 1, "100", 0, 3) }, HASTA);

            PeriodoPerdidaDesconocidaDTO enero = Mes(i, "REI", 1);
            Assert.AreEqual(3, enero.FaltaUnidades);
            Assert.AreEqual(30m, enero.FaltaValor);
            Assert.AreEqual(0, enero.FaltaCuadradaUnidades);
            Assert.AreEqual(3, enero.PerdidaRealUnidades);
            Assert.AreEqual(-30m, enero.NetoValor);
            Assert.AreEqual(3, Mes(i, "REI", 0).PerdidaRealUnidades);
            Assert.AreEqual(30m, i.Total.PerdidaRealValor);
        }

        [TestMethod]
        public void FaltaEnUnoYSobraEnOtroElMismoMes_Cuadra()
        {
            PerdidaDesconocidaDTO i = GestorPerdidaDesconocida.Construir(new[]
            {
                F("REI", 2, "100", 0, 3),
                F("ALG", 2, "100", 2, 0, "_ErrRepo")
            }, HASTA);

            PeriodoPerdidaDesconocidaDTO rei = Mes(i, "REI", 2);
            PeriodoPerdidaDesconocidaDTO alg = Mes(i, "ALG", 2);
            Assert.AreEqual(3, rei.FaltaUnidades);
            Assert.AreEqual(2, rei.FaltaCuadradaUnidades);
            Assert.AreEqual(1, rei.PerdidaRealUnidades);
            Assert.AreEqual(10m, rei.PerdidaRealValor);
            Assert.AreEqual(2, alg.SobraUnidades);
            Assert.AreEqual(2, alg.SobraCuadradaUnidades);
            Assert.AreEqual(0, alg.SobraRealUnidades);

            ProductoPerdidaDesconocidaDTO p = i.Productos.Single(x => x.Almacen == "REI");
            Assert.AreEqual(-3, p.UnidadesNeto);
            Assert.AreEqual(2, p.UnidadesCuadradas);
            CollectionAssert.AreEqual(new[] { "ALG" }, p.CuadraCon);
            Assert.AreEqual(-1, p.UnidadesSinCuadrar);
            Assert.AreEqual(-10m, p.ValorSinCuadrar);
            Assert.AreEqual("100", p.Producto);
        }

        [TestMethod]
        public void FaltaEnEneroYSobraEnMarzoEnOtroAlmacen_NoCuadraEnElMesPeroSiEnElAcumulado()
        {
            PerdidaDesconocidaDTO i = GestorPerdidaDesconocida.Construir(new[]
            {
                F("REI", 1, "100", 0, 1),
                F("ALC", 3, "100", 1, 0)
            }, HASTA);

            Assert.AreEqual(1, Mes(i, "REI", 1).PerdidaRealUnidades);
            Assert.AreEqual(1, Mes(i, "ALC", 3).SobraRealUnidades);
            Assert.AreEqual(0, Mes(i, "REI", 0).PerdidaRealUnidades);
            Assert.AreEqual(1, Mes(i, "REI", 0).FaltaCuadradaUnidades);
            Assert.AreEqual(0, i.Total.PerdidaRealUnidades);
        }

        [TestMethod]
        public void AltaYBajaDelMismoProductoEnElMismoMes_SeNetaYApareceYDesaparece()
        {
            PerdidaDesconocidaDTO i = GestorPerdidaDesconocida.Construir(new[]
            {
                F("ALG", 1, "43415", 1, 0),
                F("ALG", 1, "43415", 0, 1, "_ErrRepo")
            }, HASTA);

            Assert.AreEqual(0, Mes(i, "ALG", 1).FaltaUnidades);
            Assert.AreEqual(0, Mes(i, "ALG", 1).SobraUnidades);
            ProductoPerdidaDesconocidaDTO p = i.Productos.Single();
            Assert.IsTrue(p.ApareceYDesaparece);
            Assert.AreEqual(0, p.UnidadesNeto);
        }

        [TestMethod]
        public void OtrosDiariosAMano_NoCuentanYSalenSinClasificar()
        {
            PerdidaDesconocidaDTO i = GestorPerdidaDesconocida.Construir(new[]
            {
                F("ALG", 1, "100", 0, 2, "DiarioMermas"),
                F("ALG", 1, "200", 0, 1, "DiarioMermas", 5m),
                F("ALG", 1, "300", 4, 4, "_MontarKit")
            }, HASTA);

            Assert.AreEqual(0, i.Total.FaltaUnidades);
            Assert.AreEqual(0, i.Productos.Count);
            DiarioSinClasificarDTO d = i.DiariosSinClasificar.Single();
            Assert.AreEqual("DiarioMermas", d.Diario);
            Assert.AreEqual(-3, d.UnidadesNeto);
            Assert.AreEqual(-25m, d.ValorNeto);
            Assert.AreEqual(2, d.NumeroProductos);
        }

        [TestMethod]
        public void MesesPosterioresAHasta_NoEntran()
        {
            PerdidaDesconocidaDTO i = GestorPerdidaDesconocida.Construir(new[] { F("ALG", 4, "100", 0, 1) }, HASTA);

            Assert.AreEqual(0, i.Total.FaltaUnidades);
        }

        [TestMethod]
        public void SinPrecioMedio_ValeCero()
        {
            PerdidaDesconocidaDTO i = GestorPerdidaDesconocida.Construir(new[] { F("ALG", 1, "100", 0, 2, precio: 0m) }, HASTA);

            Assert.AreEqual(2, i.Total.FaltaUnidades);
            Assert.AreEqual(0m, i.Total.FaltaValor);
        }

        [TestMethod]
        public void Cuadrar_FaltaRepartidaEntreDosSobras_EnElOrdenAlgReiAlc()
        {
            Dictionary<string, (int Unidades, List<string> Con)> r = GestorPerdidaDesconocida.Cuadrar(new Dictionary<string, int>
            {
                ["ALC"] = 2,
                ["ALG"] = -3,
                ["REI"] = 2
            });

            Assert.AreEqual(3, r["ALG"].Unidades);
            CollectionAssert.AreEqual(new[] { "REI", "ALC" }, r["ALG"].Con);
            Assert.AreEqual(2, r["REI"].Unidades);
            Assert.AreEqual(1, r["ALC"].Unidades);
        }

        [TestMethod]
        public void CategoriaDe_LosDiarios()
        {
            Assert.AreEqual(GestorPerdidaDesconocida.Categoria.Desconocida, GestorPerdidaDesconocida.CategoriaDe("_RegInvent "));
            Assert.AreEqual(GestorPerdidaDesconocida.Categoria.Desconocida, GestorPerdidaDesconocida.CategoriaDe("_ErrRepo"));
            Assert.AreEqual(GestorPerdidaDesconocida.Categoria.Ninguna, GestorPerdidaDesconocida.CategoriaDe("_Ficticio"));
            Assert.AreEqual(GestorPerdidaDesconocida.Categoria.Ninguna, GestorPerdidaDesconocida.CategoriaDe(null));
            Assert.AreEqual(GestorPerdidaDesconocida.Categoria.SinClasificar, GestorPerdidaDesconocida.CategoriaDe("_Santi"));
        }

        [TestMethod]
        public void ResolverPeriodo_Casos()
        {
            DateTime hoy = new DateTime(2027, 5, 20, 9, 0, 0);

            Assert.IsNull(GestorPerdidaDesconocida.ResolverPeriodo(null, null, hoy, out DateTime d, out DateTime h));
            Assert.AreEqual(new DateTime(2027, 1, 1), d);
            Assert.AreEqual(new DateTime(2027, 5, 20), h);

            Assert.IsNull(GestorPerdidaDesconocida.ResolverPeriodo(2026, null, hoy, out d, out h));
            Assert.AreEqual(new DateTime(2026, 12, 31), h);

            Assert.IsNull(GestorPerdidaDesconocida.ResolverPeriodo(null, new DateTime(2027, 3, 31), hoy, out d, out h));
            Assert.AreEqual(new DateTime(2027, 1, 1), d);

            Assert.IsNotNull(GestorPerdidaDesconocida.ResolverPeriodo(2028, null, hoy, out _, out _));
            Assert.IsNotNull(GestorPerdidaDesconocida.ResolverPeriodo(2026, new DateTime(2027, 3, 31), hoy, out _, out _));
            Assert.IsNotNull(GestorPerdidaDesconocida.ResolverPeriodo(null, new DateTime(2027, 6, 1), hoy, out _, out _));
        }

        [TestMethod]
        public void Excel_SeGeneraConSusHojas()
        {
            PerdidaDesconocidaDTO i = GestorPerdidaDesconocida.Construir(new[] { F("REI", 1, "100", 0, 3), F("ALG", 1, "100", 1, 0) }, HASTA);

            byte[] bytes = new GeneradorExcelPerdidaDesconocida().GenerarBytes(i);

            // Un .xlsx es un zip: empieza por «PK».
            Assert.IsTrue(bytes.Length > 100);
            Assert.AreEqual((byte)'P', bytes[0]);
            Assert.AreEqual((byte)'K', bytes[1]);
        }
    }
}
