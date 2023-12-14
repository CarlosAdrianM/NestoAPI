using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Kits;
using NestoAPI.Models.Kits;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure
{
    [TestClass]
    public class GestorUbicacionesTests
    {
        [TestMethod]
        public void GestorUbicaciones_AsignarUbicacionesMasAntiguas_SiSoloHayUnoYCuadraLaCantidadLaAsignamos()
        {
            // Arrange
            IUbicacionService servicio = A.Fake<IUbicacionService>();
            var ubicacion = new UbicacionProductoDTO
            {
                Id = 1234,
                Cantidad = 7
            };
            A.CallTo(() => servicio.LeerUbicacionesProducto(A<string>._)).Returns(new List<UbicacionProductoDTO> { ubicacion });
            GestorUbicaciones sut = new GestorUbicaciones(servicio);
            var preExtracto = new PreExtractoProductoDTO
            {
                Cantidad = -7
            };
            var preExtractosIn = new List<PreExtractoProductoDTO> {
                preExtracto
            };

            // Act
            var preExtractosOut = sut.AsignarUbicacionesMasAntiguas(preExtractosIn).GetAwaiter().GetResult();

            // Assert
            Assert.AreEqual(1234, preExtractosOut[0].Ubicaciones[0].Id); // Usamos la misma ubicación en vez de crear una nueva
            Assert.AreEqual(-7, preExtractosOut[0].Ubicaciones[0].Cantidad); // Se le cambia el signo a la cantidad
            Assert.AreEqual(Constantes.Ubicaciones.ESTADO_REGISTRO_MONTAR_KITS, ubicacion.Estado); // La marcamos ya con estado negativo 
        }

        [TestMethod]
        public void GestorUbicaciones_AsignarUbicacionesMasAntiguas_SiLaCantidadEsMayorLaAsignaYModificaElResto()
        {
            // Arrange
            IUbicacionService servicio = A.Fake<IUbicacionService>();
            var ubicacion = new UbicacionProductoDTO
            {
                Id = 1234,
                Cantidad = 7 // ---> esta cantidad es mayor que la que se necesita
            };
            A.CallTo(() => servicio.LeerUbicacionesProducto(A<string>._)).Returns(new List<UbicacionProductoDTO> { ubicacion });
            GestorUbicaciones sut = new GestorUbicaciones(servicio);
            var preExtracto = new PreExtractoProductoDTO
            {
                Cantidad = -5 // ---> esta cantidad es menor que la de las ubicaciones devueltas
            };
            var preExtractosIn = new List<PreExtractoProductoDTO> {
                preExtracto
            };

            // Act
            var preExtractosOut = sut.AsignarUbicacionesMasAntiguas(preExtractosIn).GetAwaiter().GetResult();

            // Assert
            Assert.AreEqual(0, preExtractosOut[0].Ubicaciones[0].Id); // Usamos una ubicación nueva
            Assert.AreEqual(-5, preExtractosOut[0].Ubicaciones[0].Cantidad); // Asignamos 5
            Assert.AreEqual(Constantes.Ubicaciones.ESTADO_REGISTRO_MONTAR_KITS, preExtractosOut[0].Ubicaciones[0].Estado);
            Assert.AreEqual(1234, preExtractosOut[0].Ubicaciones[1].Id); // Modificamos la ubicación actual
            Assert.AreEqual(2, preExtractosOut[0].Ubicaciones[1].Cantidad); // Y le asignamos 2
            Assert.AreEqual(Constantes.Ubicaciones.ESTADO_A_MODIFICAR_CANTIDAD, preExtractosOut[0].Ubicaciones[1].Estado);
        }

        [TestMethod]
        public void GestorUbicaciones_AsignarUbicacionesMasAntiguas_SiLaCantidadEsMenorYNoHayMasUbicacionesDaErrorDeCantidadInsuficiente()
        {
            // Arrange
            IUbicacionService servicio = A.Fake<IUbicacionService>();
            var ubicacion = new UbicacionProductoDTO
            {
                Id = 1234,
                Cantidad = 5 // ---> esta cantidad es menor que la que se necesita
            };
            A.CallTo(() => servicio.LeerUbicacionesProducto(A<string>._)).Returns(new List<UbicacionProductoDTO> { ubicacion });
            GestorUbicaciones sut = new GestorUbicaciones(servicio);
            var preExtracto = new PreExtractoProductoDTO
            {
                Cantidad = -7 // ---> esta cantidad es mayor que la de las ubicaciones devueltas
            };
            var preExtractosIn = new List<PreExtractoProductoDTO> {
                preExtracto
            };

            // Act y Assert
            var excepcion = Assert.ThrowsException<Exception>(() => sut.AsignarUbicacionesMasAntiguas(preExtractosIn).GetAwaiter().GetResult());
            Assert.AreEqual("No hay cantidad suficiente para montar el kit", excepcion.Message);
        }

        [TestMethod]
        public void GestorUbicaciones_AsignarUbicacionesMasAntiguas_SiLaCantidadDeLaUbicacionEsMenorQueLaDelPreextractoPeroHayMasUbicacionesLoAsignaDeLaSiguiente()
        {
            // Arrange
            IUbicacionService servicio = A.Fake<IUbicacionService>();
            var ubicacion = new UbicacionProductoDTO
            {
                Id = 1234,
                Cantidad = 5 // ---> esta cantidad es menor que la que se necesita
            };
            var ubicacion2 = new UbicacionProductoDTO
            {
                Id = 1235,
                Cantidad = 3
            };
            A.CallTo(() => servicio.LeerUbicacionesProducto(A<string>._)).Returns(new List<UbicacionProductoDTO> { ubicacion, ubicacion2 });
            GestorUbicaciones sut = new GestorUbicaciones(servicio);
            var preExtracto = new PreExtractoProductoDTO
            {
                Cantidad = -7 // ---> esta cantidad es mayor que la primera pero menor que las dos juntas
            };
            var preExtractosIn = new List<PreExtractoProductoDTO> {
                preExtracto
            };

            // Act 
            var preExtractosOut = sut.AsignarUbicacionesMasAntiguas(preExtractosIn).GetAwaiter().GetResult();

            // Assert
            Assert.AreEqual(1234, preExtractosOut[0].Ubicaciones[0].Id); // Cogemos la primera entera
            Assert.AreEqual(-5, preExtractosOut[0].Ubicaciones[0].Cantidad);
            Assert.AreEqual(Constantes.Ubicaciones.ESTADO_REGISTRO_MONTAR_KITS, preExtractosOut[0].Ubicaciones[0].Estado);
            Assert.AreEqual(0, preExtractosOut[0].Ubicaciones[1].Id); // Ubicación nueva
            Assert.AreEqual(-2, preExtractosOut[0].Ubicaciones[1].Cantidad);
            Assert.AreEqual(Constantes.Ubicaciones.ESTADO_REGISTRO_MONTAR_KITS, preExtractosOut[0].Ubicaciones[1].Estado);
            Assert.AreEqual(1235, preExtractosOut[0].Ubicaciones[2].Id); // Modificamos la cantidad pendiente de la segunda
            Assert.AreEqual(1, preExtractosOut[0].Ubicaciones[2].Cantidad);
            Assert.AreEqual(Constantes.Ubicaciones.ESTADO_A_MODIFICAR_CANTIDAD, preExtractosOut[0].Ubicaciones[2].Estado);
        }

        [TestMethod]
        public void GestorUbicaciones_AsignarUbicacionesMasAntiguas_SiNoHayUbicacionesPeroSiHayCantidadCreaLaUbicacion()
        {
            // Arrange
            IUbicacionService servicio = A.Fake<IUbicacionService>();
            A.CallTo(() => servicio.LeerUbicacionesProducto(A<string>._)).Returns(new List<UbicacionProductoDTO>()); // --> No hay ubicaciones
            GestorUbicaciones sut = new GestorUbicaciones(servicio);
            var preExtracto = new PreExtractoProductoDTO
            {
                Cantidad = 1 // ---> tiene que crear la ubicación de esta cantidad
            };
            var preExtractosIn = new List<PreExtractoProductoDTO> {
                preExtracto
            };

            // Act 
            var preExtractosOut = sut.AsignarUbicacionesMasAntiguas(preExtractosIn).GetAwaiter().GetResult();

            // Assert
            Assert.AreEqual(2, preExtractosOut[0].Ubicaciones.Count); // Una para dejarla pendiente de ubicar y otra para el registro
            Assert.AreEqual(0, preExtractosOut[0].Ubicaciones[0].Id);
            Assert.AreEqual(1, preExtractosOut[0].Ubicaciones[0].Cantidad);
            Assert.AreEqual(Constantes.Ubicaciones.PENDIENTE_UBICAR, preExtractosOut[0].Ubicaciones[0].Estado);
            Assert.IsNull(preExtractosOut[0].Ubicaciones[0].Pasillo);
            Assert.IsNull(preExtractosOut[0].Ubicaciones[0].Fila);
            Assert.IsNull(preExtractosOut[0].Ubicaciones[0].Columna);
            Assert.AreEqual(0, preExtractosOut[0].Ubicaciones[1].Id); // Esta es la del registro
            Assert.AreEqual(1, preExtractosOut[0].Ubicaciones[1].Cantidad);
            Assert.AreEqual(Constantes.Ubicaciones.ESTADO_REGISTRO_MONTAR_KITS, preExtractosOut[0].Ubicaciones[1].Estado);
            Assert.IsNull(preExtractosOut[0].Ubicaciones[1].Pasillo);
            Assert.IsNull(preExtractosOut[0].Ubicaciones[1].Fila);
            Assert.IsNull(preExtractosOut[0].Ubicaciones[1].Columna);
        }

        [TestMethod]
        public void GestorUbicaciones_AsignarUbicacionesMasAntiguas_SiLaCantidadPendienteEsCeroNoCreamosUbicacion()
        {
            // Arrange
            IUbicacionService servicio = A.Fake<IUbicacionService>();
            var ubicacion = new UbicacionProductoDTO
            {
                Id = 1234,
                Cantidad = 2 // --> con esto es suficiente para la necesidad de -1
            };
            var ubicacion2 = new UbicacionProductoDTO
            {
                Id = 1235,
                Cantidad = 3 // --> estas las tenemos, pero no son necesarias
            };
            A.CallTo(() => servicio.LeerUbicacionesProducto(A<string>._)).Returns(new List<UbicacionProductoDTO> { ubicacion, ubicacion2 });            
            GestorUbicaciones sut = new GestorUbicaciones(servicio);
            var preExtracto = new PreExtractoProductoDTO
            {
                Cantidad = -1 // --> estamos montando un kit, pero es el producto asociado, el que se da de baja
            };
            var preExtractosIn = new List<PreExtractoProductoDTO> {
                preExtracto
            };

            // Act 
            var preExtractosOut = sut.AsignarUbicacionesMasAntiguas(preExtractosIn).GetAwaiter().GetResult();

            // Assert
            Assert.AreEqual(2, preExtractosOut[0].Ubicaciones.Count); // la 1234 la divide en dos, pero la 1235 la ignoramos (por eso no está)
            Assert.AreEqual(0, preExtractosOut[0].Ubicaciones[0].Id);
            Assert.AreEqual(-1, preExtractosOut[0].Ubicaciones[0].Cantidad);
            Assert.AreEqual(1234, preExtractosOut[0].Ubicaciones[1].Id);
            Assert.AreEqual(1, preExtractosOut[0].Ubicaciones[1].Cantidad);
        }

        [TestMethod]
        public void GestorUbicaciones_AsignarUbicacionesMasAntiguas_SiNoTieneUbicacionPeroLaCantidadEsNegativaTieneQueDarUnError()
        {
            // Arrange
            IUbicacionService servicio = A.Fake<IUbicacionService>();
            A.CallTo(() => servicio.LeerUbicacionesProducto(A<string>._)).Returns(new List<UbicacionProductoDTO>());
            GestorUbicaciones sut = new GestorUbicaciones(servicio);
            var preExtracto = new PreExtractoProductoDTO
            {
                Producto = "PROD",
                Cantidad = -1 // --> estamos montando un kit, pero es el producto asociado, el que se da de baja
            };
            var preExtractosIn = new List<PreExtractoProductoDTO> {
                preExtracto
            };

            // Act y Assert
            var excepcion = Assert.ThrowsException<Exception>(() => sut.AsignarUbicacionesMasAntiguas(preExtractosIn).GetAwaiter().GetResult());
            Assert.AreEqual($"El producto {preExtracto.Producto} no tiene stock", excepcion.Message);
        }
    }
}
