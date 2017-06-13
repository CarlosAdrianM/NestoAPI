using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.RecursosHumanos;

namespace NestoAPI.Tests.Models.RecursosHumanos
{
    [TestClass]
    public class ImportadorAccionesAlgeteTest
    {

        // EN ESTA CLASE METEMOS LOS TEST DE IMPORTACION QUE NO SON COMUNES A TODOS LOS ImportadorAccionesAdapter
        // PERO BÁSICAMENTE SON LO MISMO QUE LOS QUE EMPIEZAN POR GestorAcciones_Importar_xxxxxxx

        [TestMethod]
        public void ImportadorAccionesAlgete_Ejecutar_siLaEntradaTieneDosLineasDeTipoDistintoYNoEsTipoJornadaLaListaDeAccionesTendraTresElementos()
        {
            GestorAcciones gestor = new GestorAcciones();
            gestor.Importar("1, 2017-05-16 07:55:33, 2, 3\n1, 2017-05-16 07:55:33, 3, 0");
            Assert.AreEqual(3, gestor.Acciones.Count);
        }

        [TestMethod]
        public void ImportadorAccionesAlgete_Ejecutar_siLaEntradaTieneDosLineasDeTipoDistintoYSiEsTipoJornadaLaListaDeAccionesTendraUnElemento()
        {
            GestorAcciones gestor = new GestorAcciones();
            gestor.Importar("1, 2017-05-16 07:55:33, 2, I\n1, 2017-05-16 07:55:33, 3, O");
            Assert.AreEqual(1, gestor.Acciones.Count);
        }

        [TestMethod]
        public void ImportadorAccionesAlgete_Ejecutar_siLaEntradaTieneDosLineasDelMismoTipoYEsTipoJornadaLaListaDeAccionesTendraUnElemento()
        {
            GestorAcciones gestor = new GestorAcciones();
            gestor.Importar("7,2017-04-05 16:57:42,1,I\n7, 2017-04-05 16:57:43, 1,  I");
            Assert.AreEqual(1, gestor.Acciones.Count);
            Assert.AreEqual(new DateTime(2017, 04, 05, 16, 57, 42).TimeOfDay, gestor.Acciones[0].HoraInicio);
            Assert.AreEqual(new DateTime(2017, 04, 05, 16, 57, 43).TimeOfDay, gestor.Acciones[0].HoraFin);
        }

        [TestMethod]
        public void ImportadorAccionesAlgete_Ejecutar_siLaEntradaTieneDosLineasDelMismoTipoYNoEsTipoJornadaLaListaDeAccionesTendraDosElementos()
        {
            GestorAcciones gestor = new GestorAcciones();
            gestor.Importar("7,2017-04-05 16:57:42,1,0\n7, 2017-04-05 16:57:43, 1,  0");
            Assert.AreEqual(2, gestor.Acciones.Count);
            Assert.AreEqual(new DateTime(2017, 04, 05, 16, 57, 42).TimeOfDay, gestor.Acciones[1].HoraInicio);
            Assert.AreEqual(new DateTime(2017, 04, 05, 16, 57, 43).TimeOfDay, gestor.Acciones[1].HoraFin);
        }

        [TestMethod]
        public void ImportadorAccionesAlgete_Ejecutar_siLaEntradaTieneTresLineasDelMismoTipoYNoEsTipoJornadaLaListaDeAccionesTendraTresElementos()
        {
            GestorAcciones gestor = new GestorAcciones();
            gestor.Importar("7,2017-04-05 16:57:42,1,3\n7, 2017-04-05 16:57:43, 1,  3\n7, 2017-04-05 16:58:45, 1,  3");
            Assert.AreEqual(3, gestor.Acciones.Count);
            Assert.AreEqual(new DateTime(2017, 04, 05, 16, 57, 42).TimeOfDay, gestor.Acciones[1].HoraInicio);
            Assert.AreEqual(new DateTime(2017, 04, 05, 16, 58, 45).TimeOfDay, gestor.Acciones[2].HoraInicio);
        }

        [TestMethod]
        public void ImportadorAccionesAlgete_Ejecutar_siLaEntradaTieneTresLineasDelMismoTipoYSiEsTipoJornadaLaListaDeAccionesTendraUnElemento()
        {
            GestorAcciones gestor = new GestorAcciones();
            gestor.Importar("7,2017-04-05 16:57:42,1,I\n7, 2017-04-05 16:57:43, 1,  I\n7, 2017-04-05 16:58:45, 1,  I");
            Assert.AreEqual(1, gestor.Acciones.Count);
            Assert.AreEqual(new DateTime(2017, 04, 05, 16, 57, 42).TimeOfDay, gestor.Acciones[0].HoraInicio);
            Assert.AreEqual(new DateTime(2017, 04, 05, 16, 58, 45).TimeOfDay, gestor.Acciones[0].HoraFin);
        }

        [TestMethod]
        public void ImportadorAccionesAlgete_Ejecutar_siLaEntradaTieneDosLineasDelMismoTipoQueSiEsTipoJornadaPeroDistintasFechasLaListaDeAccionesTendraDosElementos()
        {
            GestorAcciones gestor = new GestorAcciones();
            gestor.Importar("7,2017-04-05 16:57:42,1,I\n7, 2017-04-06 16:57:43, 1,I");
            Assert.AreEqual(2, gestor.Acciones.Count);
        }

        [TestMethod]
        public void ImportadorAccionesAlgete_Ejecutar_siLaEntradaTieneDosLineasDelMismoTipoQueNoEsTipoJornadaPeroDistintasFechasLaListaDeAccionesTendraCuatroElementos()
        {
            GestorAcciones gestor = new GestorAcciones();
            gestor.Importar("7,2017-04-05 16:57:42,1,3\n7, 2017-04-06 16:57:43, 1,3");
            Assert.AreEqual(4, gestor.Acciones.Count);
        }
    }
}
