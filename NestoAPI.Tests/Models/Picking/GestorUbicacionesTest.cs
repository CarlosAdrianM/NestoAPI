using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.Picking;
using System.Collections.Generic;
using NestoAPI.Models;

namespace NestoAPI.Tests.Models.Picking
{
    [TestClass]
    public class GestorUbicacionesTest
    {
        [TestMethod]
        public void GestorUbicaciones_Ejecutar_siNoEstaElProductoNoCambiaLaCantidad()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                Producto = "A",
                Cantidad = 7,
                BaseImponible = 100,
                CantidadReservada = 7,
                FechaEntrega = new DateTime()
            };

            UbicacionPicking ubicacion = new UbicacionPicking
            {
                Id = 1,
                Producto = "B",
                Cantidad = 10,
                CantidadNueva = 10,
                Estado = 0,
                EstadoNuevo = 0
            };
            List<UbicacionPicking> ubicaciones = new List<UbicacionPicking>();
            ubicaciones.Add(ubicacion);

            GestorUbicaciones gestor = new GestorUbicaciones(linea, ubicaciones);
            gestor.Ejecutar();

            Assert.AreEqual(10, ubicacion.Cantidad);
            Assert.AreEqual(10, ubicacion.CantidadNueva);
            Assert.AreEqual(0, ubicacion.Estado);
            Assert.AreEqual(0, ubicacion.EstadoNuevo);
            Assert.AreEqual(0, ubicacion.LineaPedidoVentaId);
        }

        [TestMethod]
        public void GestorUbicaciones_Ejecutar_siEstaElProductoCambiaLaCantidadNuevaYElEstado()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 7,
                BaseImponible = 100,
                CantidadReservada = 7,
                FechaEntrega = new DateTime()
            };

            UbicacionPicking ubicacion = new UbicacionPicking
            {
                Id = 1,
                Producto = "A",
                Cantidad = 10,
                CantidadNueva = 10,
                Estado = 0,
                EstadoNuevo = 0
            };
            List<UbicacionPicking> ubicaciones = new List<UbicacionPicking>();
            ubicaciones.Add(ubicacion);

            GestorUbicaciones gestor = new GestorUbicaciones(linea, ubicaciones);
            gestor.Ejecutar();

            Assert.AreEqual(10, ubicacion.Cantidad);
            Assert.AreEqual(7, ubicacion.CantidadNueva);
            Assert.AreEqual(0, ubicacion.Estado);
            Assert.AreEqual(3, ubicacion.EstadoNuevo);
            Assert.AreEqual(1, ubicacion.LineaPedidoVentaId);
        }

        [TestMethod]
        public void GestorUbicaciones_Ejecutar_siLaCantidadEsDistintaCreaUnaNuevaUbicacion()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 7,
                BaseImponible = 100,
                CantidadReservada = 7,
                FechaEntrega = new DateTime()
            };

            UbicacionPicking ubicacion = new UbicacionPicking
            {
                Id = 1,
                Producto = "A",
                Cantidad = 10,
                CantidadNueva = 10,
                Estado = 0,
                EstadoNuevo = 0
            };
            List<UbicacionPicking> ubicaciones = new List<UbicacionPicking>();
            ubicaciones.Add(ubicacion);

            GestorUbicaciones gestor = new GestorUbicaciones(linea, ubicaciones);
            gestor.Ejecutar();
            UbicacionPicking ubicacionNueva = ubicaciones.FindLast(u => u.Id == 0);

            Assert.AreEqual(1, ubicacionNueva.CopiaId);
            Assert.AreEqual(3, ubicacionNueva.Cantidad);
            Assert.AreEqual(3, ubicacionNueva.CantidadNueva);
            Assert.AreEqual(0, ubicacionNueva.Estado);
            Assert.AreEqual(0, ubicacionNueva.EstadoNuevo);
            Assert.AreEqual(0, ubicacionNueva.LineaPedidoVentaId);
        }

        [TestMethod]
        public void GestorUbicaciones_Ejecutar_siLaCantidadDeLaUbicacionEsMenorDebeCogerDosUbicaciones()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 10,
                BaseImponible = 100,
                CantidadReservada = 10,
                FechaEntrega = new DateTime()
            };

            UbicacionPicking ubicacion = new UbicacionPicking
            {
                Id = 1,
                Producto = "A",
                Cantidad = 7,
                CantidadNueva = 7,
                Estado = 0,
                EstadoNuevo = 0
            };
            UbicacionPicking ubicacion2 = new UbicacionPicking
            {
                Id = 2,
                Producto = "A",
                Cantidad = 4,
                CantidadNueva = 4,
                Estado = 0,
                EstadoNuevo = 0
            };


            List<UbicacionPicking> ubicaciones = new List<UbicacionPicking>();
            ubicaciones.Add(ubicacion);
            ubicaciones.Add(ubicacion2);

            GestorUbicaciones gestor = new GestorUbicaciones(linea, ubicaciones);
            gestor.Ejecutar();

            Assert.AreEqual(7, ubicacion.Cantidad);
            Assert.AreEqual(7, ubicacion.CantidadNueva);
            Assert.AreEqual(0, ubicacion.Estado);
            Assert.AreEqual(3, ubicacion.EstadoNuevo);
            Assert.AreEqual(1, ubicacion.LineaPedidoVentaId);
            Assert.AreEqual(4, ubicacion2.Cantidad);
            Assert.AreEqual(3, ubicacion2.CantidadNueva);
            Assert.AreEqual(0, ubicacion2.Estado);
            Assert.AreEqual(3, ubicacion2.EstadoNuevo);
            Assert.AreEqual(1, ubicacion2.LineaPedidoVentaId);
        }

        [TestMethod]
        public void GestorUbicaciones_Ejecutar_siUnaUbicacionSeDivideDosVecesCogeCopiaIdEnVezDeId()
        {
            LineaPedidoPicking linea1 = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 5,
                CantidadReservada = 5
            };

            LineaPedidoPicking linea2 = new LineaPedidoPicking
            {
                Id = 1,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 4,
                CantidadReservada = 4
            };

            UbicacionPicking ubicacion = new UbicacionPicking
            {
                Id = 1,
                Producto = "A",
                Cantidad = 17,
                CantidadNueva = 17,
                Estado = 0,
                EstadoNuevo = 0
            };


            List<UbicacionPicking> ubicaciones = new List<UbicacionPicking>();
            ubicaciones.Add(ubicacion);

            GestorUbicaciones gestor1 = new GestorUbicaciones(linea1, ubicaciones);
            gestor1.Ejecutar();

            GestorUbicaciones gestor2 = new GestorUbicaciones(linea2, ubicaciones);
            gestor2.Ejecutar();

            Assert.AreEqual(3, ubicaciones.Count);
            if (ubicaciones.Count == 3)
            {
                UbicacionPicking ubicacion1 = ubicaciones[0];
                UbicacionPicking ubicacion2 = ubicaciones[1];
                UbicacionPicking ubicacion3 = ubicaciones[2];
                Assert.AreEqual(5, ubicacion1.CantidadNueva);
                Assert.AreEqual(4, ubicacion2.CantidadNueva);
                Assert.AreEqual(8, ubicacion3.CantidadNueva);
                Assert.IsTrue(ubicacion1.Id != 0 || ubicacion1.CopiaId != 0);
                Assert.IsTrue(ubicacion2.Id != 0 || ubicacion2.CopiaId != 0);
                Assert.IsTrue(ubicacion3.Id != 0 || ubicacion3.CopiaId != 0);
            }
        }


        [TestMethod]
        [ExpectedException(typeof(ArgumentException), "La ubicación está descuadrada")]
        public void GestorUbicaciones_Ejecutar_siLaUbicacionEstaDescuadradaDebeDarError()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 15,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "A",
                Cantidad = 7,
                BaseImponible = 100,
                CantidadReservada = 7,
                FechaEntrega = new DateTime()
            };

            // Hay reservadas más cantidad de las que tenemos en ubicaciones
            UbicacionPicking ubicacion = new UbicacionPicking
            {
                Id = 5,
                Producto = "A",
                Cantidad = 6,
                CantidadNueva = 6,
                Estado = 0,
                EstadoNuevo = 0
            };
            List<UbicacionPicking> ubicaciones = new List<UbicacionPicking>();
            ubicaciones.Add(ubicacion);

            GestorUbicaciones gestor = new GestorUbicaciones(linea, ubicaciones);
            gestor.Ejecutar();
        }
        
        [TestMethod]
        public void GestorUbicaciones_Ejecutar_siLaCantidadDaJustaNoHayQueInsertarNuevaUbicacion()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 15,
                Producto = "A",
                Cantidad = 7,
                BaseImponible = 100,
                CantidadReservada = 7,
                FechaEntrega = new DateTime()
            };

            // Hay reservadas más cantidad de las que tenemos en ubicaciones
            UbicacionPicking ubicacion = new UbicacionPicking
            {
                Id = 1,
                Producto = "A",
                Cantidad = 7,
                CantidadNueva = 7,
                Estado = 0,
                EstadoNuevo = 0
            };
            List<UbicacionPicking> ubicaciones = new List<UbicacionPicking>();
            ubicaciones.Add(ubicacion);

            GestorUbicaciones gestor = new GestorUbicaciones(linea, ubicaciones);
            gestor.Ejecutar();

            UbicacionPicking ubicacionNueva = ubicaciones.FindLast(u => u.Id == 0);
            Assert.IsNull(ubicacionNueva);
        }


        [TestMethod]
        public void GestorUbicaciones_Ejecutar_siLaLineaEsUnaCuentaContableNoHayQueInsertarNuevaUbicacion()
        {
            LineaPedidoPicking linea = new LineaPedidoPicking
            {
                Id = 15,
                TipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = "62400003",
                Cantidad = 1,
                BaseImponible = 6,
                CantidadReservada = 1,
                FechaEntrega = new DateTime()
            };

            List<UbicacionPicking> ubicaciones = new List<UbicacionPicking>();

            GestorUbicaciones gestor = new GestorUbicaciones(linea, ubicaciones);
            gestor.Ejecutar();

            UbicacionPicking ubicacionNueva = ubicaciones.FindLast(u => u.Id == 0);
            Assert.IsNull(ubicacionNueva);
        }

    }
}
