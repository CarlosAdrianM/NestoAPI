using System;
using System.Text;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.RecursosHumanos;

namespace NestoAPI.Tests.Models.RecursosHumanos
{
    /// <summary>
    /// Descripción resumida de GestorAccionesTest
    /// </summary>
    [TestClass]
    public class GestorAccionesTest
    {
        public GestorAccionesTest()
        {
            //
            // TODO: Agregar aquí la lógica del constructor
            //
        }

        private TestContext testContextInstance;

        /// <summary>
        ///Obtiene o establece el contexto de las pruebas que proporciona
        ///información y funcionalidad para la serie de pruebas actual.
        ///</summary>
        public TestContext TestContext
        {
            get
            {
                return testContextInstance;
            }
            set
            {
                testContextInstance = value;
            }
        }

        #region Atributos de prueba adicionales
        //
        // Puede usar los siguientes atributos adicionales conforme escribe las pruebas:
        //
        // Use ClassInitialize para ejecutar el código antes de ejecutar la primera prueba en la clase
        // [ClassInitialize()]
        // public static void MyClassInitialize(TestContext testContext) { }
        //
        // Use ClassCleanup para ejecutar el código una vez ejecutadas todas las pruebas en una clase
        // [ClassCleanup()]
        // public static void MyClassCleanup() { }
        //
        // Usar TestInitialize para ejecutar el código antes de ejecutar cada prueba 
        // [TestInitialize()]
        // public void MyTestInitialize() { }
        //
        // Use TestCleanup para ejecutar el código una vez ejecutadas todas las pruebas
        // [TestCleanup()]
        // public void MyTestCleanup() { }
        //
        #endregion

        [TestMethod]
        public void GestorAcciones_Importar_laListaDeAccionesNoEstaVacia()
        {
            GestorAcciones gestor = new GestorAcciones();
            gestor.Importar("1, 2017-05-16 07:55:33, 1, I");
            Assert.IsNotNull(gestor.Acciones);
        }

        [TestMethod]
        public void GestorAcciones_Importar_siLaEntradaSoloTieneUnaLineaYEsTipoJornadaLaListaDeAccionesTendraUnElemento()
        {
            GestorAcciones gestor = new GestorAcciones();
            gestor.Importar("1, 2017-05-16 07:55:33, 2, I");
            Assert.AreEqual(1, gestor.Acciones.Count);
        }

        [TestMethod]
        public void GestorAcciones_Importar_siLaEntradaSoloTieneUnaLineaYNoEsTipoJornadaLaListaDeAccionesTendraDosElementos()
        {
            GestorAcciones gestor = new GestorAcciones();
            gestor.Importar("1, 2017-05-16 07:55:33, 2, 3");
            Assert.AreEqual(2, gestor.Acciones.Count);
        }

        [TestMethod]
        public void GestorAcciones_Importar_seGuardanCorrectamenteLosValoresDeLaAccion()
        {
            GestorAcciones gestor = new GestorAcciones();
            gestor.Importar("1, 2017-05-16 07:55:33, 2, 3");
            Assert.AreEqual(1, gestor.Acciones[0].EmpleadoId);
            Assert.AreEqual(new DateTime(2017, 05, 16).Date, gestor.Acciones[0].Fecha);
            Assert.AreEqual(new DateTime(2017, 05, 16, 7, 55, 33).TimeOfDay, gestor.Acciones[0].HoraInicio);
        }

        [TestMethod]
        public void GestorAcciones_Importar_elTipoAccionSeGuardaCorrectamente()
        {
            GestorAcciones gestor = new GestorAcciones();
            gestor.Importar("1, 2017-05-16 07:55:33, 2, 0");
            Assert.AreEqual(2, gestor.Acciones[1].TipoAccionId);
            // Es el [1], porque el [0] se genera automáticamente para Jornada
            // 0 en reloj es 2 (comida)
        }

        [TestMethod]
        public void GestorAcciones_Importar_elPrimerMovimientoDeCadaDiaEsDeTipoJornada()
        {
            GestorAcciones gestor = new GestorAcciones();
            gestor.Importar("1, 2017-05-16 07:55:33, 2, 3");
            Assert.AreEqual(1, gestor.Acciones[0].TipoAccionId); // 1 == Jornada
        }

        [TestMethod]
        public void GestorAcciones_Importar_elSegundoMovimientoDeTipoJornadaNoInserta()
        {
            GestorAcciones gestor = new GestorAcciones();
            gestor.Importar("1, 2017-05-16 07:55:33, 2, I\n"+
                            "1, 2017-05-16 17:56:34, 2, O");
            Assert.AreEqual(1, gestor.Acciones.Count);
            Assert.AreEqual(1, gestor.Acciones[0].TipoAccionId); // 1 == Jornada
            Assert.AreEqual(new DateTime(2017, 05, 16, 7, 55, 33).TimeOfDay, gestor.Acciones[0].HoraInicio);
            Assert.AreEqual(new DateTime(2017, 05, 16, 17, 56, 34).TimeOfDay, gestor.Acciones[0].HoraFin);
        }

        [TestMethod]
        public void GestorAcciones_Importar_siHayMovimientosEntreLaJornadaSeRespetan()
        {
            GestorAcciones gestor = new GestorAcciones();
            gestor.Importar("1, 2017-05-16 07:55:33, 2, I\n"+
                            "1, 2017-05-16 14:35:53, 2, 4\n"+
                            "1, 2017-05-16 15:30:35, 2, 4\n"+
                            "1, 2017-05-16 17:56:34, 2, O");
            Assert.AreEqual(1, gestor.Acciones[0].TipoAccionId); // 1 == Jornada
            Assert.AreEqual(3, gestor.Acciones[1].TipoAccionId); // 3 == Cafe y Tabaco
            Assert.AreEqual(2, gestor.Acciones.Count);
            Assert.AreEqual(new DateTime(2017, 05, 16, 7, 55, 33).TimeOfDay, gestor.Acciones[0].HoraInicio);
            Assert.AreEqual(new DateTime(2017, 05, 16, 17, 56, 34).TimeOfDay, gestor.Acciones[0].HoraFin);
            Assert.AreEqual(new DateTime(2017, 05, 16, 14, 35, 53).TimeOfDay, gestor.Acciones[1].HoraInicio);
            Assert.AreEqual(new DateTime(2017, 05, 16, 15, 30, 35).TimeOfDay, gestor.Acciones[1].HoraFin);
        }
        
        [TestMethod]
        public void GestorAcciones_Importar_siHayDiferentesUsuariosLosControlaSeparados()
        {
            GestorAcciones gestor = new GestorAcciones();
            gestor.Importar("1, 2017-05-16 07:55:33, 2, I\n" +
                            "2, 2017-05-16 14:35:53, 2, I\n" +
                            "1, 2017-05-16 15:30:35, 2, O\n" +
                            "2, 2017-05-16 17:56:34, 2, O");
            Assert.AreEqual(2, gestor.Acciones.Count);
            Assert.AreEqual(1, gestor.Acciones[0].TipoAccionId); // 1 == Jornada
            Assert.AreEqual(1, gestor.Acciones[1].TipoAccionId); // 1 == Jornada
            Assert.AreEqual(new DateTime(2017, 05, 16, 7, 55, 33).TimeOfDay, gestor.Acciones[0].HoraInicio);
            Assert.AreEqual(new DateTime(2017, 05, 16, 15, 30, 35).TimeOfDay, gestor.Acciones[0].HoraFin);
            Assert.AreEqual(new DateTime(2017, 05, 16, 14, 35, 53).TimeOfDay, gestor.Acciones[1].HoraInicio);
            Assert.AreEqual(new DateTime(2017, 05, 16, 17, 56, 34).TimeOfDay, gestor.Acciones[1].HoraFin);
        }

        [TestMethod]
        public void GestorAcciones_Importar_siHayDosMovimientosSeguidosQueSonDeTipoJornadaDejaSoloUno()
        {
            GestorAcciones gestor = new GestorAcciones();
            gestor.Importar("1, 2017-05-16 07:55:33, 2, I\n" +
                            "1, 2017-05-16 07:55:34, 2, I");
            Assert.AreEqual(1, gestor.Acciones.Count);
        }



    }
}
