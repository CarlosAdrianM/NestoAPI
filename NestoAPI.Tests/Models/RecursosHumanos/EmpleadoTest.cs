using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.RecursosHumanos;

namespace NestoAPI.Tests.Models.RecursosHumanos
{
    [TestClass]
    public class EmpleadoTest
    {
        [TestMethod]
        public void Empleado_JornadaBruta_sumaLasHorasDelGestorDeAcciones()
        {
            Empleado empleado = new Empleado()
            {
                Id = 1
            };
            GestorAcciones gestorAcciones = new GestorAcciones();
            gestorAcciones.Importar("1, 2017-05-16 07:58:55, 2, I\n" +
                                    "1, 2017-05-16 17:00:00, 2,O\n");
            empleado.Acciones = gestorAcciones.Acciones;
            Assert.AreEqual(9, empleado.JornadaBruta.Hours);
            Assert.AreEqual(1, empleado.JornadaBruta.Minutes);
            Assert.AreEqual(5, empleado.JornadaBruta.Seconds);
        }

        [TestMethod]
        public void Empleado_JornadaPausada_sumaLasHorasDelGestorDeAcciones()
        {
            Empleado empleado = new Empleado()
            {
                Id = 1
            };
            GestorAcciones gestorAcciones = new GestorAcciones();
            gestorAcciones.Importar("1, 2017-05-16 08:00:00, 2, I\n" +
                                    "1, 2017-05-16 14:30:00, 2, 4\n" +
                                    "1, 2017-05-16 15:20:15, 2, 4\n" +
                                    "1, 2017-05-16 17:00:00, 2,O\n");
            empleado.Acciones = gestorAcciones.Acciones;
            Assert.AreEqual(0, empleado.JornadaPausada.Hours);
            Assert.AreEqual(50, empleado.JornadaPausada.Minutes);
            Assert.AreEqual(15, empleado.JornadaPausada.Seconds);
        }

        [TestMethod]
        public void Empleado_JornadaNeta_restaLaJornadaPausadaALaJornadaBruta()
        {
            Empleado empleado = new Empleado()
            {
                Id = 1
            };
            GestorAcciones gestorAcciones = new GestorAcciones();
            gestorAcciones.Importar("1, 2017-05-16 08:00:00, 2, I\n" +
                                    "1, 2017-05-16 14:30:00, 2, 4\n" +
                                    "1, 2017-05-16 15:20:15, 2, 4\n" +
                                    "1, 2017-05-16 17:00:00, 2,O\n");
            empleado.Acciones = gestorAcciones.Acciones;
            Assert.AreEqual(8, empleado.JornadaNeta.Hours);
            Assert.AreEqual(9, empleado.JornadaNeta.Minutes);
            Assert.AreEqual(45, empleado.JornadaNeta.Seconds);
        }

        [TestMethod]
        public void Empleado_DuracionEstimada_laCalculaEntreLasDelMismoTipo()
        {
            Empleado empleado = new Empleado()
            {
                Id = 1
            };
            GestorAcciones gestorAcciones = new GestorAcciones();
            gestorAcciones.Importar("1, 2017-05-16 08:00:00, 2, I\n" +
                                    "1, 2017-05-16 10:00:00, 2, 4\n" + //4 == Cafe
                                    "1, 2017-05-16 10:20:00, 2, 4\n" +
                                    "1, 2017-05-16 17:00:00, 2,O\n"+
                                    "1, 2017-05-17 08:00:00, 2, I\n" +
                                    "1, 2017-05-17 10:00:00, 2, 4\n" +
                                    "1, 2017-05-17 10:10:00, 2, 4\n" +
                                    "1, 2017-05-17 17:00:00, 2,O\n");
            empleado.Acciones = gestorAcciones.Acciones;
            Assert.AreEqual(0, empleado.DuracionEstimada(3).Hours); // 3 == CafeTabaco
            Assert.AreEqual(15, empleado.DuracionEstimada(3).Minutes);
            Assert.AreEqual(0, empleado.DuracionEstimada(3).Seconds);
        }

        [TestMethod]
        public void Empleado_DuracionEstimada_laCalculaSoloEntreLasQueSiEstanCompletas()
        {
            Empleado empleado = new Empleado()
            {
                Id = 1
            };
            GestorAcciones gestorAcciones = new GestorAcciones();
            gestorAcciones.Importar("1, 2017-05-16 08:00:00, 2, I\n" +
                                    "1, 2017-05-16 10:00:00, 2, 4\n" + //4 == Cafe
                                    "1, 2017-05-16 10:20:00, 2, 4\n" +
                                    "1, 2017-05-16 17:00:00, 2,O\n" +
                                    "1, 2017-05-17 08:00:00, 2, I\n" +
                                    "1, 2017-05-17 10:00:00, 2, 4\n" +
                                    "1, 2017-05-17 00:00:00, 2, 4\n" +
                                    "1, 2017-05-17 17:00:00, 2,O\n");
            empleado.Acciones = gestorAcciones.Acciones;
            Assert.AreEqual(0, empleado.DuracionEstimada(3).Hours); // 3 == CafeTabaco
            Assert.AreEqual(20, empleado.DuracionEstimada(3).Minutes);
            Assert.AreEqual(0, empleado.DuracionEstimada(3).Seconds);
        }

        [TestMethod]
        public void Empleado_JornadaLaboral_laCalculaSoloEntreLasQueSiEstanCompletas()
        {
            Empleado empleado = new Empleado()
            {
                Id = 1,
                HorasSemana = 15
            };
            GestorAcciones gestorAcciones = new GestorAcciones();
            gestorAcciones.Importar("1, 2017-05-09 08:00:00, 2, I\n" +
                                    "1, 2017-05-09 10:00:00, 2, 4\n" + //4 == Cafe
                                    "1, 2017-05-09 10:20:00, 2, 4\n" +
                                    "1, 2017-05-09 17:15:00, 2,O\n" +
                                    "1, 2017-05-26 08:00:00, 2, I\n" +
                                    "1, 2017-05-26 10:00:00, 2, 4\n" +
                                    "1, 2017-05-26 10:40:00, 2, 4\n" +
                                    "1, 2017-05-26 17:00:35, 2,O\n");
            empleado.Acciones = gestorAcciones.Acciones;

            Assert.AreEqual(1, empleado.JornadaLaboral.Days); // 3 == CafeTabaco
            Assert.AreEqual(21, empleado.JornadaLaboral.Hours); // 3 == CafeTabaco
            Assert.AreEqual(0, empleado.JornadaLaboral.Minutes);
            Assert.AreEqual(0, empleado.JornadaLaboral.Seconds);
        }

        [TestMethod]
        public void Empleado_SaldoJornada_laCalculaSoloEntreLasQueSiEstanCompletas()
        {
            Empleado empleado = new Empleado()
            {
                Id = 1,
                HorasSemana = 15
            };
            GestorAcciones gestorAcciones = new GestorAcciones();
            gestorAcciones.Importar("1, 2017-05-16 08:00:00, 2, I\n" +
                                    "1, 2017-05-16 10:00:00, 2, 4\n" + //4 == Cafe
                                    "1, 2017-05-16 10:20:00, 2, 4\n" +
                                    "1, 2017-05-16 17:15:00, 2,O\n" +
                                    "1, 2017-05-17 08:00:00, 2, I\n" +
                                    "1, 2017-05-17 10:00:00, 2, 4\n" +
                                    "1, 2017-05-17 10:40:00, 2, 4\n" +
                                    "1, 2017-05-17 17:00:35, 2,O\n");
            empleado.Acciones = gestorAcciones.Acciones;
            Assert.AreEqual(2, empleado.SaldoJornada.Hours); // 3 == CafeTabaco
            Assert.AreEqual(15, empleado.SaldoJornada.Minutes);
            Assert.AreEqual(35, empleado.SaldoJornada.Seconds);
        }

        [TestMethod]
        public void Empleado_RatioEstimacion_calculaElEstado2SobreElTotal()
        {
            Empleado empleado = new Empleado()
            {
                Id = 1
            };
            GestorAcciones gestorAcciones = new GestorAcciones();
            gestorAcciones.Importar("1, 2017-05-16 08:00:00, 2, I\n" +
                                    "1, 2017-05-16 10:00:00, 2, 4\n" + //4 == Cafe
                                    "1, 2017-05-16 10:20:00, 2, 4\n" +
                                    "1, 2017-05-16 17:00:00, 2,O\n" +
                                    "1, 2017-05-17 08:00:00, 2, I\n" +
                                    "1, 2017-05-17 10:00:00, 2, 4\n" +
                                    "1, 2017-05-17 00:00:00, 2, 4\n" +
                                    "1, 2017-05-17 17:00:00, 2,O\n");
            empleado.Acciones = gestorAcciones.Acciones;
            empleado.Acciones[3].Estado = 2;

            Assert.AreEqual(4, empleado.Acciones.Count);
            Assert.AreEqual(0.25M, empleado.RatioEstimacion); 
            
        }
    }
}
