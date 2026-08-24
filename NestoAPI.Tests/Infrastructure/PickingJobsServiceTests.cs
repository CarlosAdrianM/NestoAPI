using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Reflection;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#361: contrato del job del picking de cierre. No se puede ejecutar de verdad
    /// (toca la BD de arriba abajo), pero sí se fijan las decisiones que lo hacen seguro y que no
    /// se ven leyendo el cuerpo por encima.
    ///
    /// Los atributos se comprueban POR NOMBRE: el proyecto de tests no referencia Hangfire y no
    /// merece la pena añadir la dependencia solo para esto.
    /// </summary>
    [TestClass]
    public class PickingJobsServiceTests
    {
        private static MethodInfo Metodo()
        {
            return typeof(NestoAPI.Infraestructure.Picking.PickingJobsService)
                .GetMethod("SacarPickingDeCierre", BindingFlags.Public | BindingFlags.Static);
        }

        private static Attribute Atributo(string nombre)
        {
            return Metodo().GetCustomAttributes(false)
                .Cast<Attribute>()
                .FirstOrDefault(a => a.GetType().Name == nombre);
        }

        [TestMethod]
        public void SacarPickingDeCierre_EsPublicoYEstatico_ParaQueHangfireLoPuedaInvocar()
        {
            // Hangfire serializa la llamada como tipo + método; si deja de ser público y estático,
            // el job recurrente se queda registrado pero revienta al dispararse.
            Assert.IsNotNull(Metodo(), "Hangfire invoca este método por reflexión desde Startup");
        }

        [TestMethod]
        public void SacarPickingDeCierre_NoTieneReintentoAutomatico()
        {
            // DELIBERADO. No es por riesgo (el picking es idempotente: las líneas ya asignadas se
            // filtran por Picking == null || Picking == 0), sino para no mandarle al almacén un
            // correo por cada intento. Si falla, se ve en rojo en el dashboard y se relanza con un
            // clic; llegar tarde no cuesta nada desde que el horizonte se declara.
            Attribute retry = Atributo("AutomaticRetryAttribute");
            Assert.IsNotNull(retry, "Sin el atributo, Hangfire reintenta 10 veces = 10 correos al almacén");

            int attempts = (int)retry.GetType().GetProperty("Attempts").GetValue(retry);
            Assert.AreEqual(0, attempts);
        }

        [TestMethod]
        public void SacarPickingDeCierre_NoSePuedeEjecutarDosVecesALaVez()
        {
            // El picking mueve stock; dos ejecuciones solapadas (p. ej. el job de las 11h y un
            // disparo manual desde api/Picking/Automatico) no deben pisarse.
            Assert.IsNotNull(Atributo("DisableConcurrentExecutionAttribute"));
        }

        [TestMethod]
        public void SacarPickingDeCierre_DevuelveLosPedidos_ParaQueElEndpointManualLosPuedaMostrar()
        {
            // El endpoint api/Picking/Automatico delega en ESTE método para que no haya dos
            // implementaciones del picking de cierre; necesita la lista para responderla.
            Assert.AreEqual(
                typeof(System.Collections.Generic.List<NestoAPI.Models.Picking.PedidoPicking>),
                Metodo().ReturnType);
        }
    }
}
