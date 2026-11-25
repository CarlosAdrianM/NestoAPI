using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Inventarios;
using NestoAPI.Infraestructure.Reposiciones;
using System;
using System.Linq;
using System.Net;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Incidencia 1455 (Paloma, 25/11/26): al enviar una reposición de Alcobendas, el disparador del inventario paró prdExtrProducto
    /// por una fila pendiente de OTRO producto y Ariadna enseñó el error de SQL. LA REGLA: qué filas se enseñan y cómo.
    /// </summary>
    [TestClass]
    public class ErrorInventarioPendienteReposicionTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 25);

        private const string ERROR_EXTRACTO = "Error al contabilizar el diario: Se esta realizando el inventario del producto " +
            "La transacción terminó en el desencadenador. Se anuló el lote.";
        private const string ERROR_UBICACIONES = "Se esta realizando el inventario del producto1";

        private static FilaInventarioSinContabilizar Fila(int orden, string producto, string almacen = "ALC", DateTime? fecha = null,
            string usuario = "NUEVAVISION\\Paloma", string descripcion = null)
        {
            return new FilaInventarioSinContabilizar
            {
                NumeroOrden = orden, Almacen = almacen, Producto = producto, Descripcion = descripcion ?? "PRODUCTO " + producto,
                Fecha = fecha ?? HOY.AddDays(-1), Usuario = usuario
            };
        }

        [TestMethod]
        public void EsErrorDeInventario_ConYSinProductoYDentro()
        {
            Assert.IsTrue(ErrorInventarioPendiente.EsErrorDeInventario(new Exception(ERROR_EXTRACTO)));
            Assert.IsTrue(ErrorInventarioPendiente.EsErrorDeInventario(new Exception(ERROR_UBICACIONES)));
            Assert.IsTrue(ErrorInventarioPendiente.EsErrorDeInventario(new NestoBusinessException("Error al contabilizar el diario",
                new Exception("Se esta realizando el inventario del producto"))));
            Assert.IsFalse(ErrorInventarioPendiente.EsErrorDeInventario(new Exception("No se puede realizar la acción. Se quedaría un stock negativo.")));
            Assert.IsFalse(ErrorInventarioPendiente.EsErrorDeInventario(null));
        }

        [TestMethod]
        public void Mensaje_UnaFila_ElCasoDePaloma()
        {
            string mensaje = ErrorInventarioPendienteReposicion.Mensaje(new[] { Fila(1, "24870", descripcion: "TANGA CABALLERO NEGRO ") },
                HOY, ErrorInventarioPendienteReposicion.FINAL_ENVIAR);

            Assert.AreEqual("No se puede enviar la reposición: en Alcobendas hay inventario sin contabilizar (24870 TANGA CABALLERO NEGRO, " +
                "del 24/11, de Paloma). Contabilícelo o borre esa fila en Inventario y vuelva a enviarla; lo leído sigue guardado.", mensaje);
        }

        [TestMethod]
        public void Mensaje_VariasFilas_ConHuecoYDeOtroAnio()
        {
            FilaInventarioSinContabilizar conHueco = Fila(2, "39822", almacen: "ALG", fecha: new DateTime(2025, 12, 30), usuario: "pedro");
            conHueco.Pasillo = "005";
            conHueco.Fila = "002";
            conHueco.Columna = "001";

            string mensaje = ErrorInventarioPendienteReposicion.Mensaje(new[] { Fila(1, "24870"), conHueco }, HOY,
                ErrorInventarioPendienteReposicion.FINAL_ENVIAR);

            StringAssert.StartsWith(mensaje, "No se puede enviar la reposición: en Alcobendas y Algete hay inventario sin contabilizar (" +
                "24870 PRODUCTO 24870, del 24/11, de Paloma; 39822 PRODUCTO 39822, del 30/12/2025, en 005-002-001, de pedro). " +
                "Contabilícelo o borre esas filas");
        }

        [TestMethod]
        public void Mensaje_MasDeDiez_PoneElTopeYCuantasMas()
        {
            string mensaje = ErrorInventarioPendienteReposicion.Mensaje(Enumerable.Range(1, 13).Select(n => Fila(n, "P" + n)), HOY,
                ErrorInventarioPendienteReposicion.FINAL_ENVIAR);

            StringAssert.Contains(mensaje, "P10 PRODUCTO P10, del 24/11, de Paloma; … y 3 más)");
            Assert.IsFalse(mensaje.Contains("P11 "));
        }

        [TestMethod]
        public void Mensaje_SinFilas_EsNull()
        {
            Assert.IsNull(ErrorInventarioPendienteReposicion.Mensaje(new FilaInventarioSinContabilizar[0], HOY, null));
            Assert.IsNull(ErrorInventarioPendienteReposicion.Mensaje(null, HOY, null));
        }

        [TestMethod]
        public void Acotar_SoloLosAlmacenesDeLaReposicion_YPrimeroSusProductos()
        {
            var pendientes = new[]
            {
                Fila(1, "24870", fecha: HOY.AddDays(-2)),
                Fila(2, "41262", fecha: HOY.AddDays(-1)),
                Fila(3, "41262", almacen: "REI"),
                Fila(4, "11111", almacen: "ALG")
            };

            CollectionAssert.AreEqual(new[] { 2, 1 }, ErrorInventarioPendienteReposicion.Acotar(pendientes, new[] { "ALC" }, new[] { "41262 " })
                .Select(f => f.NumeroOrden).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2 }, ErrorInventarioPendienteReposicion.Acotar(pendientes, new[] { "ALC" }, new[] { "99999" })
                .Select(f => f.NumeroOrden).ToArray(), "Si no casa ninguna, todas las del almacén, por fecha");
        }

        [TestMethod]
        public void Traducir_DeNegocio_ConservaElCodigoYLlevaElOriginalDentro()
        {
            var original = new NestoBusinessException(ERROR_EXTRACTO) { StatusCode = HttpStatusCode.Conflict };

            Exception traducida = ErrorInventarioPendienteReposicion.Traducir(original, new[] { Fila(1, "24870") }, new[] { "ALC" },
                new[] { "41262" }, HOY, ErrorInventarioPendienteReposicion.FINAL_ENVIAR);

            NestoBusinessException negocio = traducida as NestoBusinessException;
            Assert.IsNotNull(negocio);
            Assert.AreEqual(HttpStatusCode.Conflict, negocio.StatusCode);
            Assert.AreSame(original, negocio.InnerException);
            StringAssert.StartsWith(negocio.Message, ErrorInventarioPendienteReposicion.INICIO);
        }

        [TestMethod]
        public void Traducir_QueNoEraDeNegocio_SigueSinSerlo()
        {
            Exception traducida = ErrorInventarioPendienteReposicion.Traducir(new Exception(ERROR_EXTRACTO), new[] { Fila(1, "24870") },
                new[] { "ALC" }, null, HOY, null);

            Assert.IsInstanceOfType(traducida, typeof(ReposicionInventarioPendienteException));
            Assert.IsFalse(traducida is NestoBusinessException);
        }

        [TestMethod]
        public void Traducir_SinFilasDelAlmacen_SinLecturaOOtroError_DejaElOriginal()
        {
            var original = new NestoBusinessException(ERROR_EXTRACTO);

            Assert.IsNull(ErrorInventarioPendienteReposicion.Traducir(original, new FilaInventarioSinContabilizar[0], new[] { "ALC" }, null, HOY, null));
            Assert.IsNull(ErrorInventarioPendienteReposicion.Traducir(original, new[] { Fila(1, "1", almacen: "ALG") }, new[] { "ALC" }, null, HOY, null));
            Assert.IsNull(ErrorInventarioPendienteReposicion.Traducir(original, null, new[] { "ALC" }, null, HOY, null), "null = no se pudo leer");
            Assert.IsNull(ErrorInventarioPendienteReposicion.Traducir(new NestoBusinessException("stock negativo"), new[] { Fila(1, "1") },
                new[] { "ALC" }, null, HOY, null));
        }

        [TestMethod]
        public void NucleoTraducir_SiMontarElMensajeFalla_DejaElOriginal()
        {
            Assert.IsNull(ErrorInventarioPendiente.Traducir(new Exception(ERROR_EXTRACTO), () => throw new InvalidOperationException("caída"),
                (m, e) => new Exception(m, e)));
        }
    }
}
