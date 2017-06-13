using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.RecursosHumanos;

namespace NestoAPI.Tests.Models.RecursosHumanos
{
    [TestClass]
    public class AccionTest
    {
        [TestMethod]
        public void Accion_Duracion_devuelveLaDiferenciaEntreElInicioYElFinDeLaAccion()
        {
            Accion accion = new Accion()
            {
                HoraInicio = new TimeSpan(1, 0, 0),
                HoraFin = new TimeSpan(2, 15, 33)
            };
            Assert.AreEqual(1, accion.Duracion.Hours);
            Assert.AreEqual(15, accion.Duracion.Minutes);
            Assert.AreEqual(33, accion.Duracion.Seconds);
        }
    }
}
