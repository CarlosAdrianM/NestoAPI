using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.RecursosHumanos;
using System.Collections.Generic;

namespace NestoAPI.Tests.Models.RecursosHumanos
{
    [TestClass]
    public class GestorEmpleadosTest
    {
        [TestMethod]
        public void GestorEmpleados_RellenarEmpleados_seCreanTodosLosEmpleados()
        {
            Empleado empleado = new Empleado() {
                Id =1
            };
            List<Empleado> listaEmpleados = new List<Empleado>();
            listaEmpleados.Add(empleado);
            GestorAcciones gestorAcciones = new GestorAcciones();
            GestorEmpleados gestorEmpleados = new GestorEmpleados(gestorAcciones);
            gestorEmpleados.RellenarEmpleados(listaEmpleados);
            Assert.AreEqual(1, gestorEmpleados.listaEmpleados.Count);
        }

        [TestMethod]
        public void GestorEmpleados_RellenarEmpleados_seRellenanLasListasDeAccionesDeLosEmpleados()
        {
            Empleado empleado = new Empleado() {
                Id = 1
            };
            Empleado empleado2 = new Empleado() {
                Id = 2
            };
            List<Empleado> listaEmpleados = new List<Empleado>();
            listaEmpleados.Add(empleado);
            listaEmpleados.Add(empleado2);
            GestorAcciones gestorAcciones = new GestorAcciones();
            gestorAcciones.Importar("1, 2017-05-16 07:55:33, 2, I\n" +
                                    "2, 2017-05-16 14:35:53, 2, I\n" +
                                    "1, 2017-05-16 15:30:35, 2, O\n" +
                                    "2, 2017-05-16 17:56:34, 2, O");
            GestorEmpleados gestorEmpleados = new GestorEmpleados(gestorAcciones);
            gestorEmpleados.RellenarEmpleados(listaEmpleados);
            Assert.AreEqual(1, gestorEmpleados.listaEmpleados[0].Acciones.Count);
            Assert.AreEqual(1, gestorEmpleados.listaEmpleados[1].Acciones.Count);
        }

        [TestMethod]
        public void GestorEmpleados_Verificar_seRellenaLasHorasFinalesDeLasDuracionesNegativas()
        {
            Empleado empleado = new Empleado()
            {
                Id = 1
            };
            Empleado empleado2 = new Empleado()
            {
                Id = 2
            };
            List<Empleado> listaEmpleados = new List<Empleado>();
            listaEmpleados.Add(empleado);
            listaEmpleados.Add(empleado2);
            GestorAcciones gestorAcciones = new GestorAcciones();
            gestorAcciones.Importar("1, 2017-05-16 08:00:00, 2, I\n" +
                                    "1, 2017-05-16 10:00:00, 2, 4\n" + //4 == Cafe
                                    "1, 2017-05-16 10:20:00, 2, 4\n" +
                                    "1, 2017-05-16 17:00:00, 2,O\n" +
                                    "2, 2017-05-16 08:00:00, 2, I\n" +
                                    "2, 2017-05-16 10:00:00, 2, 4\n" + //4 == Cafe
                                    "2, 2017-05-16 10:40:00, 2, 4\n" +
                                    "2, 2017-05-16 17:00:00, 2,O\n" +
                                    "1, 2017-05-17 08:00:00, 2, I\n" +
                                    "1, 2017-05-17 10:00:00, 2, 4\n" +
                                    "1, 2017-05-17 00:00:00, 2, 4\n" +
                                    "1, 2017-05-17 17:00:00, 2,O\n"+ 
                                    "1, 2017-05-18 08:00:00, 2, I\n" +
                                    "1, 2017-05-18 10:00:00, 2, 4\n" + //4 == Cafe
                                    "1, 2017-05-18 10:10:00, 2, 4\n" +
                                    "1, 2017-05-18 17:00:00, 2,O\n");
            GestorEmpleados gestorEmpleados = new GestorEmpleados(gestorAcciones);
            gestorEmpleados.RellenarEmpleados(listaEmpleados);
            gestorEmpleados.Verificar();

            Assert.AreEqual(2, gestorEmpleados.listaEmpleados[0].Acciones[3].Estado);
            Assert.AreEqual(0, gestorEmpleados.listaEmpleados[0].Acciones[3].HoraFin.Hours);
            Assert.AreEqual(15, gestorEmpleados.listaEmpleados[0].Acciones[3].HoraFin.Minutes);
            Assert.AreEqual(0, gestorEmpleados.listaEmpleados[0].Acciones[3].HoraFin.Seconds);
        }
    }
}
