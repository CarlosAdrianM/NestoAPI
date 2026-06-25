using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosCompra;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Tests del generador QuestPDF de la "ORDEN DE COMPRA" a proveedores (migración RDLC -> backend,
    /// Nesto#340/#386). Verifican que se produce un PDF válido (firma %PDF) en los escenarios clave.
    /// La carga del logo se inyecta para no depender de la red.
    /// </summary>
    [TestClass]
    public class GeneradorPdfPedidoCompraTests
    {
        private GeneradorPdfPedidoCompra _generador;

        [TestInitialize]
        public void Setup()
        {
            // Sin logo (null) -> el generador reserva el hueco y no pinta nada; PDF igualmente válido.
            _generador = new GeneradorPdfPedidoCompra(() => null);
        }

        [TestMethod]
        public void GenerarPdf_PedidoValorado_DevuelvePdfValido()
        {
            var pedido = PedidoEjemplo(valorado: true);

            var resultado = _generador.GenerarPdf(pedido);

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0, "El PDF debe tener contenido");
            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        public void GenerarPdf_PedidoNoValorado_DevuelvePdfValido()
        {
            // Sin valorar: no se muestran precios. No debe romper aunque los importes vengan a cero.
            var pedido = PedidoEjemplo(valorado: false);

            var resultado = _generador.GenerarPdf(pedido);

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0);
            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        public void GenerarPdf_SinLineas_DevuelvePdfValido()
        {
            var pedido = PedidoEjemplo(valorado: true);
            pedido.Lineas = new List<LineaPedidoCompraInformeDTO>();

            var resultado = _generador.GenerarPdf(pedido);

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0, "Un pedido sin líneas también debe generar un PDF válido");
            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        public void GenerarPdf_LineasNull_NoLanzaYDevuelvePdf()
        {
            var pedido = PedidoEjemplo(valorado: true);
            pedido.Lineas = null;

            var resultado = _generador.GenerarPdf(pedido);

            Assert.IsNotNull(resultado);
            byte[] bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0);
            ComprobarCabeceraPdf(bytes);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void GenerarPdf_PedidoNull_Lanza()
        {
            _generador.GenerarPdf(null);
        }

        private static PedidoCompraInformeDTO PedidoEjemplo(bool valorado)
        {
            return new PedidoCompraInformeDTO
            {
                Id = 54321,
                Proveedor = "30001",
                Nombre = "DISTRIBUCIONES EJEMPLO, S.L.",
                Direccion = "C/ del Comercio, 25",
                CodigoPostal = "28001",
                Poblacion = "Madrid",
                Provincia = "Madrid",
                Telefono = "915551234",
                Cif = "B12345678",
                Fecha = new DateTime(2026, 6, 25),
                PedidoValorado = valorado,
                Lineas = new List<LineaPedidoCompraInformeDTO>
                {
                    new LineaPedidoCompraInformeDTO
                    {
                        SuReferencia = "REF-A", NuestraReferencia = "38697",
                        Descripcion = "Crema hidratante facial", Tamanno = 250, UnidadMedida = "Ud",
                        Cantidad = 12, PrecioUnitario = 8.50m, SumaDescuentos = 0.05m, BaseImponible = 96.90m
                    },
                    new LineaPedidoCompraInformeDTO
                    {
                        SuReferencia = "REF-B", NuestraReferencia = "12345",
                        Descripcion = "Champú anticaspa", Tamanno = 500, UnidadMedida = "Ud",
                        Cantidad = 6, PrecioUnitario = 4.20m, SumaDescuentos = 0m, BaseImponible = 25.20m
                    }
                }
            };
        }

        // Los PDF empiezan por la firma "%PDF" (0x25 0x50 0x44 0x46).
        private static void ComprobarCabeceraPdf(byte[] bytes)
        {
            Assert.IsTrue(bytes.Length >= 4, "El PDF es demasiado corto");
            Assert.AreEqual(0x25, bytes[0]);
            Assert.AreEqual(0x50, bytes[1]);
            Assert.AreEqual(0x44, bytes[2]);
            Assert.AreEqual(0x46, bytes[3]);
        }
    }
}
