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
            GestorFestivos gestorFestivos = new GestorFestivos();
            GestorEmpleados gestorEmpleados = new GestorEmpleados(gestorAcciones, gestorFestivos);
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
            GestorFestivos gestorFestivos = new GestorFestivos();
            GestorEmpleados gestorEmpleados = new GestorEmpleados(gestorAcciones, gestorFestivos);
            gestorEmpleados.RellenarEmpleados(listaEmpleados);
            Assert.AreEqual(1, gestorEmpleados.listaEmpleados[0].Acciones.Count);
            Assert.AreEqual(1, gestorEmpleados.listaEmpleados[1].Acciones.Count);
        }

        [TestMethod]
        public void GestorEmpleados_RellenarEmpleados_seRellenanLasListasDeFestivosDeLosEmpleados()
        {
            Empleado empleado = new Empleado()
            {
                Id = 1,
                FiestasLocales = TipoFestivo.Algete
            };
            Empleado empleado2 = new Empleado()
            {
                Id = 2,
                FiestasLocales = TipoFestivo.Madrid
            };
            List<Empleado> listaEmpleados = new List<Empleado>();
            listaEmpleados.Add(empleado);
            listaEmpleados.Add(empleado2);
            GestorAcciones gestorAcciones = new GestorAcciones();
            gestorAcciones.Importar("1, 2017-05-16 07:55:33, 2, I\n" +
                        "2, 2017-05-16 14:35:53, 2, I\n" +
                        "1, 2017-05-16 15:30:35, 2, O\n" +
                        "2, 2017-05-16 17:56:34, 2, O");
            GestorFestivos gestorFestivos = new GestorFestivos();
            Festivo festivoMadrid = new Festivo()
            {
                Fecha = new DateTime(2017, 5, 15),
                TipoFestivo = TipoFestivo.Madrid
            };
            gestorFestivos.ListaFestivos.Add(festivoMadrid);
            GestorEmpleados gestorEmpleados = new GestorEmpleados(gestorAcciones, gestorFestivos);
            gestorEmpleados.RellenarEmpleados(listaEmpleados);
            Assert.AreEqual(0, gestorEmpleados.listaEmpleados[0].Festivos.Count);
            Assert.AreEqual(1, gestorEmpleados.listaEmpleados[1].Festivos.Count);
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
            GestorFestivos gestorFestivos = new GestorFestivos();
            GestorEmpleados gestorEmpleados = new GestorEmpleados(gestorAcciones, gestorFestivos);
            gestorEmpleados.RellenarEmpleados(listaEmpleados);
            gestorEmpleados.Verificar();

            Assert.AreEqual(2, gestorEmpleados.listaEmpleados[0].Acciones[3].Estado);
            Assert.AreEqual(0, gestorEmpleados.listaEmpleados[0].Acciones[3].HoraFin.Hours);
            Assert.AreEqual(15, gestorEmpleados.listaEmpleados[0].Acciones[3].HoraFin.Minutes);
            Assert.AreEqual(0, gestorEmpleados.listaEmpleados[0].Acciones[3].HoraFin.Seconds);
        }

        [TestMethod]
        public void GestorEmpleados_Verificar_siHayUnDiaConDosJornadasSeJuntanEnUnaSola()
        {
            Empleado empleado = new Empleado()
            {
                Id = 1
            };
            List<Empleado> listaEmpleados = new List<Empleado>();
            listaEmpleados.Add(empleado);

            GestorAcciones gestorAcciones = new GestorAcciones();
            gestorAcciones.Importar("1, 2017-05-16 08:00:00, 2, I\n" +
                                    "1, 2017-05-16 09:00:00, 2, 4\n" + //4 == Cafe
                                    "1, 2017-05-16 10:00:00, 2, 4\n" +
                                    "1, 2017-05-16 11:00:00, 2,O\n" +
                                    "1, 2017-05-16 12:00:00, 2, I\n" +
                                    "1, 2017-05-16 13:00:00, 2, 4\n" + //4 == Cafe
                                    "1, 2017-05-16 14:00:00, 2, 4\n" +
                                    "1, 2017-05-16 17:00:00, 2,O\n");
            GestorFestivos gestorFestivos = new GestorFestivos();
            GestorEmpleados gestorEmpleados = new GestorEmpleados(gestorAcciones, gestorFestivos);
            gestorEmpleados.RellenarEmpleados(listaEmpleados);
            gestorEmpleados.Verificar();

            Assert.AreEqual(3, gestorEmpleados.listaEmpleados[0].Acciones.Count);
            Assert.AreEqual(8, gestorEmpleados.listaEmpleados[0].Acciones[0].HoraInicio.Hours);
            Assert.AreEqual(0, gestorEmpleados.listaEmpleados[0].Acciones[0].HoraInicio.Minutes);
            Assert.AreEqual(0, gestorEmpleados.listaEmpleados[0].Acciones[0].HoraInicio.Seconds);
            Assert.AreEqual(17, gestorEmpleados.listaEmpleados[0].Acciones[0].HoraFin.Hours);
            Assert.AreEqual(0, gestorEmpleados.listaEmpleados[0].Acciones[0].HoraFin.Minutes);
            Assert.AreEqual(0, gestorEmpleados.listaEmpleados[0].Acciones[0].HoraFin.Seconds);
        }

        [TestMethod]
        public void GestorEmpleados_Verificar_siUnMovimientoEsAnuladoCasiDeInmediatoNoLoImportamos()
        {
            Empleado empleado = new Empleado()
            {
                Id = 5
            };
            List<Empleado> listaEmpleados = new List<Empleado>();
            listaEmpleados.Add(empleado);

            GestorAcciones gestorAcciones = new GestorAcciones();
            gestorAcciones.Importar("5,2017-05-18 13:05:03,1,6\n" +
                                    "5,2017-05-18 13:05:04,1,6\n" +
                                    "5,2017-05-18 13:05:12,1,I\n");
            GestorFestivos gestorFestivos = new GestorFestivos();
            GestorEmpleados gestorEmpleados = new GestorEmpleados(gestorAcciones, gestorFestivos);
            gestorEmpleados.RellenarEmpleados(listaEmpleados);
            gestorEmpleados.Verificar();

            Assert.AreEqual(1, gestorEmpleados.listaEmpleados[0].Acciones.Count);
            Assert.AreEqual(1, gestorEmpleados.listaEmpleados[0].Acciones[0].TipoAccionId);
            Assert.AreEqual(13, gestorEmpleados.listaEmpleados[0].Acciones[0].HoraInicio.Hours);
            Assert.AreEqual(5, gestorEmpleados.listaEmpleados[0].Acciones[0].HoraInicio.Minutes);
            Assert.AreEqual(12, gestorEmpleados.listaEmpleados[0].Acciones[0].HoraInicio.Seconds);
        }

        [TestMethod]
        public void GestorEmpleados_RellenarJornadas_seCreaUnaJornadaPorCadaDia()
        {
            Empleado empleado = new Empleado()
            {
                Id = 1, 
                FiestasLocales = TipoFestivo.Madrid
            };
            List<Empleado> listaEmpleados = new List<Empleado>();
            listaEmpleados.Add(empleado);

            GestorAcciones gestorAcciones = new GestorAcciones();
            gestorAcciones.Importar("1, 2017-05-03 08:00:00, 2, I\n" +
                                    "1, 2017-05-03 09:00:00, 2, 4\n" + //4 == Cafe
                                    "1, 2017-05-03 10:00:00, 2, 4\n" +
                                    "1, 2017-05-03 11:00:00, 2,O\n" +
                                    "1, 2017-05-05 12:00:00, 2, I\n" +
                                    "1, 2017-05-05 13:00:00, 2, 4\n" + //4 == Cafe
                                    "1, 2017-05-05 14:00:00, 2, 4\n" +
                                    "1, 2017-05-05 17:00:00, 2,O\n");
            GestorFestivos gestorFestivos = new GestorFestivos();
            
            Festivo festivoMadrid = new Festivo()
            {
                Fecha = new DateTime(2017, 5, 4),
                TipoFestivo = TipoFestivo.Madrid
            };
            gestorFestivos.ListaFestivos.Add(festivoMadrid);
            
            GestorEmpleados gestorEmpleados = new GestorEmpleados(gestorAcciones, gestorFestivos);
            gestorEmpleados.RellenarEmpleados(listaEmpleados);
            gestorEmpleados.Verificar();
            gestorEmpleados.RellenarJornadas();

            Assert.AreEqual(7, gestorEmpleados.listaEmpleados[0].Jornadas.Count);
            Assert.AreEqual(TipoJornada.Laboral, gestorEmpleados.listaEmpleados[0].Jornadas[0].TipoJornada);
            Assert.AreEqual(TipoJornada.Laboral, gestorEmpleados.listaEmpleados[0].Jornadas[1].TipoJornada);
            Assert.AreEqual(TipoJornada.Laboral, gestorEmpleados.listaEmpleados[0].Jornadas[2].TipoJornada);
            Assert.AreEqual(TipoJornada.Festivo, gestorEmpleados.listaEmpleados[0].Jornadas[3].TipoJornada);
            Assert.AreEqual(TipoJornada.Laboral, gestorEmpleados.listaEmpleados[0].Jornadas[4].TipoJornada);
            Assert.AreEqual(TipoJornada.FinDeSemana, gestorEmpleados.listaEmpleados[0].Jornadas[5].TipoJornada);
            Assert.AreEqual(TipoJornada.FinDeSemana, gestorEmpleados.listaEmpleados[0].Jornadas[6].TipoJornada);
        }
    }
}
