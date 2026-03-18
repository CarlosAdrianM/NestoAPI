using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using System;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infraestructure.Notificaciones
{
    [TestClass]
    public class ServicioNotificacionesPushTests
    {
        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public async Task RegistrarDispositivo_ConTokenNull_LanzaArgumentException()
        {
            var servicio = new ServicioNotificacionesPush();
            var registro = new RegistrarDispositivoDTO
            {
                Token = null,
                Plataforma = "Android",
                Aplicacion = "NestoApp"
            };

            await servicio.RegistrarDispositivo(registro, "usuario");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public async Task RegistrarDispositivo_ConTokenVacio_LanzaArgumentException()
        {
            var servicio = new ServicioNotificacionesPush();
            var registro = new RegistrarDispositivoDTO
            {
                Token = "  ",
                Plataforma = "Android",
                Aplicacion = "NestoApp"
            };

            await servicio.RegistrarDispositivo(registro, "usuario");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public async Task RegistrarDispositivo_ConPlataformaVacia_LanzaArgumentException()
        {
            var servicio = new ServicioNotificacionesPush();
            var registro = new RegistrarDispositivoDTO
            {
                Token = "token123",
                Plataforma = "",
                Aplicacion = "NestoApp"
            };

            await servicio.RegistrarDispositivo(registro, "usuario");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public async Task RegistrarDispositivo_ConAplicacionVacia_LanzaArgumentException()
        {
            var servicio = new ServicioNotificacionesPush();
            var registro = new RegistrarDispositivoDTO
            {
                Token = "token123",
                Plataforma = "Android",
                Aplicacion = ""
            };

            await servicio.RegistrarDispositivo(registro, "usuario");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public async Task RegistrarDispositivo_ConRegistroNull_LanzaArgumentException()
        {
            var servicio = new ServicioNotificacionesPush();

            await servicio.RegistrarDispositivo(null, "usuario");
        }

        [TestMethod]
        public async Task DesregistrarDispositivo_ConTokenNull_DevuelveFalse()
        {
            var servicio = new ServicioNotificacionesPush();

            bool resultado = await servicio.DesregistrarDispositivo(null);

            Assert.IsFalse(resultado);
        }

        [TestMethod]
        public async Task DesregistrarDispositivo_ConTokenVacio_DevuelveFalse()
        {
            var servicio = new ServicioNotificacionesPush();

            bool resultado = await servicio.DesregistrarDispositivo("  ");

            Assert.IsFalse(resultado);
        }
    }
}
