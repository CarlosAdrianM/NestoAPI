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
            empleado.RellenarJornadas(new DateTime(2017,5,15), new DateTime(2017,5,21));
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
            empleado.RellenarJornadas(new DateTime(2017, 5, 15), new DateTime(2017, 5, 21));
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
            empleado.RellenarJornadas(new DateTime(2017, 5, 15), new DateTime(2017, 5, 21));
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
        public void Empleado_JornadaLaboral_siNoHayNingunaFiestaCuentaSemanasCompletas()
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
        public void Empleado_JornadaLaboral_siHayUnaFiestaLocalLeRestaEseTiempoDeLaJornada()
        {
            Empleado empleado = new Empleado()
            {
                Id = 1,
                HorasSemana = 40,
                FiestasLocales = TipoFestivo.Madrid
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

            Festivo festivo11 = new Festivo()
            {
                Fecha = new DateTime(2017, 5, 15),
                Fiesta = "Fiesta local Madrid (San Isidro)",
                TipoFestivo = TipoFestivo.Madrid
            };
            empleado.Festivos.Add(festivo11);

            Assert.AreEqual(112, empleado.JornadaLaboral.TotalHours); 
        }

        [TestMethod]
        public void Empleado_JornadaLaboral_siHayUnaFiestaElLunesAnteriorALaPrimeraAccionLaTieneEnCuenta()
        {
            Empleado empleado = new Empleado()
            {
                Id = 1,
                HorasSemana = 40,
                FiestasLocales = TipoFestivo.Madrid
            };
            GestorAcciones gestorAcciones = new GestorAcciones();
            gestorAcciones.Importar("1, 2017-05-03 08:00:00, 2, I\n" +
                                    "1, 2017-05-03 10:00:00, 2, 4\n" + //4 == Cafe
                                    "1, 2017-05-03 10:20:00, 2, 4\n" +
                                    "1, 2017-05-03 17:15:00, 2,O\n" +
                                    "1, 2017-05-25 08:00:00, 2, I\n" +
                                    "1, 2017-05-25 10:00:00, 2, 4\n" +
                                    "1, 2017-05-25 10:40:00, 2, 4\n" +
                                    "1, 2017-05-25 17:00:35, 2,O\n");
            empleado.Acciones = gestorAcciones.Acciones;

            Festivo anterior = new Festivo()
            {
                Fecha = new DateTime(2017, 5, 1),
                Fiesta = "Lunes 1 de mayo",
                TipoFestivo = TipoFestivo.Madrid
            };
            empleado.Festivos.Add(anterior);
            Festivo posterior = new Festivo()
            {
                Fecha = new DateTime(2017, 5, 26),
                Fiesta = "Viernes 26 de mayo",
                TipoFestivo = TipoFestivo.Madrid
            };
            empleado.Festivos.Add(posterior);

            Assert.AreEqual(144, empleado.JornadaLaboral.TotalHours);
        }

        [TestMethod]
        public void Empleado_JornadaLaboral_siTrabajaUnFinDeSemanaSeComputanLasHoras()
        {
            Empleado empleado = new Empleado()
            {
                Id = 1,
                HorasSemana = 40,
                FiestasLocales = TipoFestivo.Madrid
            };
            GestorAcciones gestorAcciones = new GestorAcciones();
            gestorAcciones.Importar("1, 2017-05-03 08:00:00, 2, I\n" +
                                    "1, 2017-05-03 10:00:00, 2, 4\n" + //4 == Cafe
                                    "1, 2017-05-03 10:20:00, 2, 4\n" +
                                    "1, 2017-05-03 17:00:00, 2,O\n" +
                                    "1, 2017-05-05 08:00:00, 2, I\n" +
                                    "1, 2017-05-05 10:00:00, 2, 4\n" +
                                    "1, 2017-05-05 10:40:00, 2, 4\n" +
                                    "1, 2017-05-05 17:00:00, 2,O\n");
            empleado.Acciones = gestorAcciones.Acciones;

            JornadasEspeciales formacion = new JornadasEspeciales()
            {
                Fecha = new DateTime(2017, 5, 7),
                Motivo = "Formación en domingo",
                Duracion = new TimeSpan(2, 0, 0)
            };
            empleado.DiasNoLaborables.Add(formacion);

            empleado.RellenarJornadas(new DateTime(2017,5,1), new DateTime(2017,5,7));

            Assert.AreEqual(40, empleado.JornadaLaboral.TotalHours);
            Assert.AreEqual(20, empleado.JornadaBruta.TotalHours);
        }
        
        [TestMethod]
        public void Empleado_JornadaLaboral_siHayVacacionesLasRestaDeLaJornada()
        {
            Empleado empleado = new Empleado()
            {
                Id = 1,
                HorasSemana = 40,
                FiestasLocales = TipoFestivo.Madrid
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

            Vacaciones vacaciones = new Vacaciones()
            {
                Fecha = new DateTime(2017, 5, 10)
            };
            empleado.DiasNoLaborables.Add(vacaciones);
            
            Assert.AreEqual(112, empleado.JornadaLaboral.TotalHours);
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
            empleado.RellenarJornadas(new DateTime(2017, 5, 15), new DateTime(2017, 5, 21));
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
        
        [TestMethod]
        public void Empleado_RellenarJornadas_siHayUnaFiestaSeMarcaComoFestivo()
        {
            Empleado empleado = new Empleado()
            {
                Id = 1,
                HorasSemana = 40,
                FiestasLocales = TipoFestivo.Madrid
            };
            GestorAcciones gestorAcciones = new GestorAcciones();
            gestorAcciones.Importar("1, 2017-05-03 08:00:00, 2, I\n" +
                                    "1, 2017-05-03 10:00:00, 2, 4\n" + //4 == Cafe
                                    "1, 2017-05-03 10:20:00, 2, 4\n" +
                                    "1, 2017-05-03 17:15:00, 2,O\n" +
                                    "1, 2017-05-05 08:00:00, 2, I\n" +
                                    "1, 2017-05-05 10:00:00, 2, 4\n" +
                                    "1, 2017-05-05 10:40:00, 2, 4\n" +
                                    "1, 2017-05-05 17:00:35, 2,O\n");
            empleado.Acciones = gestorAcciones.Acciones;

            Festivo anterior = new Festivo()
            {
                Fecha = new DateTime(2017, 5, 1),
                Fiesta = "Lunes de Fiesta",
                TipoFestivo = TipoFestivo.Madrid
            };
            empleado.Festivos.Add(anterior);
            Festivo posterior = new Festivo()
            {
                Fecha = new DateTime(2017, 5, 4),
                Fiesta = "Jueves de Fiesta",
                TipoFestivo = TipoFestivo.Madrid
            };
            empleado.Festivos.Add(posterior);

            DateTime fechaInicial = new DateTime(2017, 5, 1);
            DateTime fechaFinal = new DateTime(2017, 5, 6);
            Empleado.AjustarFechasSemana(ref fechaInicial, ref fechaFinal);
            empleado.RellenarJornadas(fechaInicial, fechaFinal);

            Assert.AreEqual(7, empleado.Jornadas.Count);
            Assert.AreEqual(TipoJornada.Festivo, empleado.Jornadas[0].TipoJornada);
            Assert.AreEqual(TipoJornada.Laboral, empleado.Jornadas[1].TipoJornada);
            Assert.AreEqual(TipoJornada.Laboral, empleado.Jornadas[1].TipoJornada);
            Assert.AreEqual(TipoJornada.Festivo, empleado.Jornadas[3].TipoJornada);
            Assert.AreEqual(TipoJornada.Laboral, empleado.Jornadas[4].TipoJornada);
            Assert.AreEqual(TipoJornada.FinDeSemana, empleado.Jornadas[5].TipoJornada);
            Assert.AreEqual(TipoJornada.FinDeSemana, empleado.Jornadas[6].TipoJornada);
        }


        [TestMethod]
        public void Empleado_RellenarJornadas_siHayUnaJornadaEspecialLaTieneEnCuenta()
        {
            Empleado empleado = new Empleado()
            {
                Id = 1,
                HorasSemana = 40,
                FiestasLocales = TipoFestivo.Madrid
            };
            GestorAcciones gestorAcciones = new GestorAcciones();
            gestorAcciones.Importar("1, 2017-05-03 08:00:00, 2, I\n" +
                                    "1, 2017-05-03 10:00:00, 2, 4\n" + //4 == Cafe
                                    "1, 2017-05-03 10:20:00, 2, 4\n" +
                                    "1, 2017-05-03 17:00:00, 2,O\n" +
                                    "1, 2017-05-05 08:00:00, 2, I\n" +
                                    "1, 2017-05-05 10:00:00, 2, 4\n" +
                                    "1, 2017-05-05 10:40:00, 2, 4\n" +
                                    "1, 2017-05-05 17:00:00, 2,O\n");
            empleado.Acciones = gestorAcciones.Acciones;

            JornadasEspeciales formacion = new JornadasEspeciales()
            {
                Fecha = new DateTime(2017, 5, 7),
                Motivo = "Formacion en domingo",
                Duracion = new TimeSpan(4, 0, 0)
            };
            empleado.DiasNoLaborables.Add(formacion);

            DateTime fechaInicial = new DateTime(2017, 5, 1);
            DateTime fechaFinal = new DateTime(2017, 5, 6);
            Empleado.AjustarFechasSemana(ref fechaInicial, ref fechaFinal);
            empleado.RellenarJornadas(fechaInicial, fechaFinal);

            Assert.AreEqual(1, empleado.DiasNoLaborables.Count);
            Assert.AreEqual(4, empleado.Jornadas[6].JornadaBruta.TotalHours);
        }

    }
}
