using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using System;
using System.Transactions;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#182: ELMAH escribe en la MISMA base de datos que el negocio. Si el error se loguea
    /// desde dentro de un TransactionScope (PutPedidoVenta llamado por UnirPedidos, facturación,
    /// copia de facturas...), su conexión se alista en la transacción ambiente y falla con "The
    /// underlying provider failed on Open", perdiéndose el error justo cuando más falta hace.
    /// El fix envuelve la escritura en TransactionScope(Suppress).
    /// </summary>
    [TestClass]
    public class ElmahHelperTests
    {
        [TestMethod]
        public void SinTransaccion_DentroDeUnTransactionScope_LaEscrituraNoVeLaTransaccionAmbiente()
        {
            // Este es el corazón del arreglo: dentro del scope la transacción ambiente existe,
            // pero la acción de logueo debe ejecutarse SIN ella (Current == null).
            Transaction transaccionVistaDentro = new CommittableTransaction(); // valor inicial != null
            bool seEjecuto = false;

            using (TransactionScope scope = new TransactionScope())
            {
                Assert.IsNotNull(Transaction.Current, "Precondición: dentro del scope hay transacción ambiente");

                ElmahHelper.SinTransaccion(() =>
                {
                    seEjecuto = true;
                    transaccionVistaDentro = Transaction.Current;
                });

                Assert.IsNotNull(Transaction.Current, "Al volver, la transacción ambiente sigue intacta");
            }

            Assert.IsTrue(seEjecuto, "La acción de logueo debe ejecutarse");
            Assert.IsNull(transaccionVistaDentro,
                "La escritura a ELMAH debe verse FUERA de la transacción: si no, su conexión se alista y peta");
        }

        [TestMethod]
        public void SinTransaccion_SinTransaccionAmbiente_FuncionaIgual()
        {
            bool seEjecuto = false;

            ElmahHelper.SinTransaccion(() => seEjecuto = true);

            Assert.IsTrue(seEjecuto);
        }

        [TestMethod]
        public void SinTransaccion_SiElLogueoFalla_NoLanza()
        {
            // El logueo NUNCA puede romper el flujo que lo invoca (una factura no se puede caer
            // porque ELMAH no esté disponible).
            ElmahHelper.SinTransaccion(() => throw new InvalidOperationException("ELMAH caído"));
        }

        [TestMethod]
        public void SinTransaccion_AccionNull_NoLanza()
        {
            ElmahHelper.SinTransaccion(null);
        }

        [TestMethod]
        public void Log_YSeñalar_ConExcepcionNull_NoLanzan()
        {
            ElmahHelper.Log(null);
            ElmahHelper.Log(null, "Sistema (test)");
            ElmahHelper.Señalar(null);
        }

        [TestMethod]
        public void Log_DentroDeUnTransactionScope_NoLanza()
        {
            // Sin BD de ELMAH configurada en tests, lo que se comprueba es que el camino completo
            // (incluido el Suppress) no revienta ni propaga.
            using (TransactionScope scope = new TransactionScope())
            {
                ElmahHelper.Log(new Exception("prueba dentro de transacción"), "Sistema (test)");
                ElmahHelper.Señalar(new Exception("prueba dentro de transacción"));
            }
        }
    }
}
