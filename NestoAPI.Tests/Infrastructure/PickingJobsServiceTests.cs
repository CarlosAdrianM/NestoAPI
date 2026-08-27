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
        private static MethodInfo MetodoEndpoint()
        {
            return typeof(NestoAPI.Infraestructure.Picking.PickingJobsService)
                .GetMethod("SacarPickingDeCierre", BindingFlags.Public | BindingFlags.Static);
        }

        private static MethodInfo MetodoJob()
        {
            return typeof(NestoAPI.Infraestructure.Picking.PickingJobsService)
                .GetMethod("SacarPickingDeCierreJob", BindingFlags.Public | BindingFlags.Static);
        }

        private static Attribute AtributoDelJob(string nombre)
        {
            return MetodoJob().GetCustomAttributes(false)
                .Cast<Attribute>()
                .FirstOrDefault(a => a.GetType().Name == nombre);
        }

        [TestMethod]
        public void SacarPickingDeCierreJob_EsPublicoYEstatico_ParaQueHangfireLoPuedaInvocar()
        {
            // Hangfire serializa la llamada como tipo + método; si deja de ser público y estático,
            // el job recurrente se queda registrado pero revienta al dispararse.
            Assert.IsNotNull(MetodoJob(), "Hangfire invoca este método por reflexión desde Startup");
        }

        [TestMethod]
        public void SacarPickingDeCierreJob_DevuelveVoid_ElResultadoNoDebeViajarAlEstadoDeHangfire()
        {
            // NestoAPI#416. Hangfire serializa el VALOR DE RETORNO del job y lo escribe dentro de
            // la misma transacción que marca el estado Succeeded. En la primera ejecución real
            // (26/08/2026, job 89877) el picking salió, pero la lista completa de pedidos con sus
            // líneas reventó ese commit: la transacción quedó "completada pero sin desechar" en la
            // conexión dedicada del worker, los 10 reintentos del cambio de estado reutilizaron la
            // conexión envenenada y el job acabó en Failed SIN que fallara nada del negocio (y sin
            // aviso al almacén). Es el único job de la casa que devolvía un objeto gordo; todos
            // los demás (void o resultado trivial) nunca han fallado el marcado.
            Assert.IsNotNull(MetodoJob(), "Ver SacarPickingDeCierreJob_EsPublicoYEstatico");
            Assert.AreEqual(typeof(void), MetodoJob().ReturnType,
                "El resultado del picking solo lo necesita el endpoint manual, nunca Hangfire");
        }

        [TestMethod]
        public void SacarPickingDeCierreJob_NoTieneReintentoAutomatico()
        {
            // DELIBERADO. No es por riesgo (el picking es idempotente: las líneas ya asignadas se
            // filtran por Picking == null || Picking == 0), sino para no mandarle al almacén un
            // correo por cada intento. Si falla, se ve en rojo en el dashboard y se relanza con un
            // clic; llegar tarde no cuesta nada desde que el horizonte se declara.
            Attribute retry = AtributoDelJob("AutomaticRetryAttribute");
            Assert.IsNotNull(retry, "Sin el atributo, Hangfire reintenta 10 veces = 10 correos al almacén");

            int attempts = (int)retry.GetType().GetProperty("Attempts").GetValue(retry);
            Assert.AreEqual(0, attempts);
        }

        [TestMethod]
        public void SacarPickingDeCierreJob_NoSePuedeEjecutarDosVecesALaVez()
        {
            // Dos disparos solapados DESDE HANGFIRE no deben pisarse. (La protección real contra
            // cualquier solape —incluido el endpoint manual, al que los filtros de Hangfire no
            // aplican— es el applock de GestorPicking, NestoAPI#405; este atributo solo evita que
            // Hangfire llegue a encolar el segundo.)
            Assert.IsNotNull(AtributoDelJob("DisableConcurrentExecutionAttribute"));
        }

        [TestMethod]
        public void SacarPickingDeCierre_DevuelveLosPedidos_ParaQueElEndpointManualLosPuedaMostrar()
        {
            // El endpoint api/Picking/Automatico delega en ESTE método para que no haya dos
            // implementaciones del picking de cierre; necesita la lista para responderla.
            Assert.AreEqual(
                typeof(System.Collections.Generic.List<NestoAPI.Models.Picking.PedidoPicking>),
                MetodoEndpoint().ReturnType);
        }
    }
}
