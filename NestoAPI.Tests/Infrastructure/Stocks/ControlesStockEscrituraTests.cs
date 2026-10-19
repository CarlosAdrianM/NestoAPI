using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Stocks;
using NestoAPI.Models;
using System;

namespace NestoAPI.Tests.Infrastructure.Stocks
{
    /// <summary>NestoAPI#667: solo Algete tiene stock mínimo; en las tiendas solo hay máximo.</summary>
    [TestClass]
    public class ControlesStockEscrituraTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 10, 19, 10, 0, 0);

        [TestMethod]
        public void MinimoDelAlmacen_EnAlgete_SeQuedaElMinimo()
        {
            Assert.AreEqual(7, ControlesStockEscritura.MinimoDelAlmacen("ALG", 7));
            Assert.AreEqual(7, ControlesStockEscritura.MinimoDelAlmacen("ALG ", 7));
        }

        [TestMethod]
        public void MinimoDelAlmacen_EnLasTiendas_EsCero()
        {
            Assert.AreEqual(0, ControlesStockEscritura.MinimoDelAlmacen("REI", 1));
            Assert.AreEqual(0, ControlesStockEscritura.MinimoDelAlmacen("ALC", 5));
            Assert.AreEqual(0, ControlesStockEscritura.MinimoDelAlmacen(null, 5));
        }

        [TestMethod]
        public void CambiarMaximoMinimo_EnUnaTienda_GrabaElMaximoYMinimoCero()
        {
            var control = new ControlStock { Empresa = "1", Almacén = "REI", Número = "40519", StockMínimo = 0, StockMáximo = 1 };

            ControlesStockEscritura.CambiarMaximoMinimo(control, 1, 2, "NUEVAVISION\\Manuel", AHORA);

            Assert.AreEqual((short)0, control.StockMínimo);
            Assert.AreEqual((short)2, control.StockMáximo);
        }

        [TestMethod]
        public void CambiarMaximoMinimo_EnAlgete_GrabaLosDos()
        {
            var control = new ControlStock { Empresa = "1", Almacén = "ALG", Número = "40519" };

            ControlesStockEscritura.CambiarMaximoMinimo(control, 42, 72, "NUEVAVISION\\Manuel", AHORA);

            Assert.AreEqual((short)42, control.StockMínimo);
            Assert.AreEqual((short)72, control.StockMáximo);
        }

        [TestMethod]
        public void Nuevo_EnUnaTienda_NaceConMinimoCero()
        {
            ControlStock control = ControlesStockEscritura.Nuevo("1", "ALC", "40519", 3, 4, 1, "API", AHORA);

            Assert.AreEqual((short)0, control.StockMínimo);
            Assert.AreEqual((short)4, control.StockMáximo);
        }
    }
}
