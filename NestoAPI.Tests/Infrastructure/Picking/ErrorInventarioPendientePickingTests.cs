using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Picking;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Picking
{
    /// <summary>
    /// Incidencias 1227 y 1057 (12/11/26): el picking de rutas cortado por «Se esta realizando el inventario del producto1» dice
    /// ahora qué filas de inventario están pendientes y de quién.
    /// </summary>
    [TestClass]
    public class ErrorInventarioPendientePickingTests
    {
        private const string ERROR_UBICACIONES = "Se esta realizando el inventario del producto1\r\nLa transacción terminó en el desencadenador. Se anuló el lote.";

        private static FilaInventarioPendientePicking Fila(int numero, string producto, string almacen = "ALG", DateTime? fecha = null,
            string pasillo = null, string usuario = "NUEVAVISION\\Alfredo", string descripcion = null) => new FilaInventarioPendientePicking
        {
            NumeroOrden = numero,
            Empresa = "1",
            Almacen = almacen,
            Fecha = fecha ?? new DateTime(2026, 11, 11),
            Producto = producto,
            Descripcion = descripcion ?? "PRODUCTO " + producto,
            Pasillo = pasillo,
            Fila = pasillo == null ? null : "002",
            Columna = pasillo == null ? null : "001",
            Usuario = usuario
        };

        private static List<KeyValuePair<string, string>> DelPicking(params string[] productos) =>
            productos.Select(p => new KeyValuePair<string, string>("ALG", p)).ToList();

        [TestMethod]
        public void Mensaje_ConYSinUbicacion_ConUsuarioSinDominio()
        {
            string mensaje = ErrorInventarioPendientePicking.Mensaje(new[]
            {
                Fila(948803, "39822", fecha: new DateTime(2026, 11, 12), pasillo: "005", usuario: "NUEVAVISION\\pedro", descripcion: "CHAMPÚ"),
                Fila(948700, "21075", descripcion: "CARTUCHO VINOTHERAPYA")
            });

            Assert.AreEqual(ErrorInventarioPendientePicking.INICIO + " Filas pendientes: " +
                "21075 CARTUCHO VINOTHERAPYA (ALG, 11/11/2026, sin ubicación, contó Alfredo); " +
                "39822 CHAMPÚ (ALG, 12/11/2026, 005-002-001, contó pedro). " + ErrorInventarioPendientePicking.FINAL, mensaje);
        }

        [TestMethod]
        public void Mensaje_SinUsuario_NoDiceConto()
        {
            StringAssert.Contains(ErrorInventarioPendientePicking.Mensaje(new[] { Fila(1, "21075", usuario: "  ") }),
                "(ALG, 11/11/2026, sin ubicación)");
        }

        [TestMethod]
        public void Mensaje_MasDeDiez_EnsenaDiezYCuantasMas()
        {
            string mensaje = ErrorInventarioPendientePicking.Mensaje(Enumerable.Range(1, 13).Select(n => Fila(n, "P" + n)));

            Assert.AreEqual(10, mensaje.Split(new[] { "contó Alfredo" }, StringSplitOptions.None).Length - 1);
            StringAssert.Contains(mensaje, "; … y 3 más. ");
            StringAssert.Contains(mensaje, "P10 ");
            Assert.IsFalse(mensaje.Contains("P11 "));
        }

        [TestMethod]
        public void Mensaje_SinFilas_Null()
        {
            Assert.IsNull(ErrorInventarioPendientePicking.Mensaje(new FilaInventarioPendientePicking[0]));
        }

        [TestMethod]
        public void EsErrorDeInventario_ConYSinElUno_YDentroDeOtraExcepcion()
        {
            Assert.IsTrue(ErrorInventarioPendientePicking.EsErrorDeInventario(new Exception(ERROR_UBICACIONES)));
            Assert.IsTrue(ErrorInventarioPendientePicking.EsErrorDeInventario(
                new Exception("An error occurred while updating the entries.", new Exception("Se esta realizando el inventario del producto"))));
            Assert.IsFalse(ErrorInventarioPendientePicking.EsErrorDeInventario(new Exception("Ya se está sacando otro picking en este momento.")));
            Assert.IsFalse(ErrorInventarioPendientePicking.EsErrorDeInventario(null));
        }

        [TestMethod]
        public void Acotar_SoloLosProductosDelPicking_YNuncaREI()
        {
            var pendientes = new[] { Fila(1, "21075"), Fila(2, "39822"), Fila(3, "21075", almacen: "REI"), Fila(4, "21075", almacen: "ALC") };

            List<FilaInventarioPendientePicking> acotadas = ErrorInventarioPendientePicking.Acotar(pendientes, DelPicking("21075 "));

            CollectionAssert.AreEqual(new[] { 1 }, acotadas.Select(f => f.NumeroOrden).ToArray());
        }

        [TestMethod]
        public void Acotar_NingunaDelPicking_TodasLasPendientesMenosREI()
        {
            var pendientes = new[] { Fila(1, "21075"), Fila(2, "39822", almacen: "REI") };

            CollectionAssert.AreEqual(new[] { 1 }, ErrorInventarioPendientePicking.Acotar(pendientes, DelPicking("99999")).Select(f => f.NumeroOrden).ToArray());
            CollectionAssert.AreEqual(new[] { 1 }, ErrorInventarioPendientePicking.Acotar(pendientes, null).Select(f => f.NumeroOrden).ToArray());
        }

        [TestMethod]
        public void Traducir_ErrorDelDisparador_MensajeClaroYElOriginalDentro()
        {
            var original = new Exception(ERROR_UBICACIONES);

            Exception traducida = ErrorInventarioPendientePicking.Traducir(original, () => new[] { Fila(1, "21075") }, DelPicking("21075"));

            Assert.IsInstanceOfType(traducida, typeof(PickingInventarioPendienteException));
            StringAssert.StartsWith(traducida.Message, ErrorInventarioPendientePicking.INICIO);
            StringAssert.Contains(traducida.Message, "21075 PRODUCTO 21075 (ALG, 11/11/2026, sin ubicación, contó Alfredo)");
            Assert.AreSame(original, traducida.InnerException);
        }

        [TestMethod]
        public void Traducir_FallaLaConsulta_NullParaDejarElOriginal()
        {
            Assert.IsNull(ErrorInventarioPendientePicking.Traducir(new Exception(ERROR_UBICACIONES),
                () => throw new InvalidOperationException("timeout"), DelPicking("21075")));
        }

        [TestMethod]
        public void Traducir_SinFilasPendientes_NullParaDejarElOriginal()
        {
            Assert.IsNull(ErrorInventarioPendientePicking.Traducir(new Exception(ERROR_UBICACIONES),
                () => new FilaInventarioPendientePicking[0], DelPicking("21075")));
        }

        [TestMethod]
        public void Traducir_OtroError_NiSiquieraConsulta()
        {
            bool consultado = false;

            Assert.IsNull(ErrorInventarioPendientePicking.Traducir(new Exception("Otro error"),
                () => { consultado = true; return new[] { Fila(1, "21075") }; }, null));
            Assert.IsFalse(consultado);
        }
    }
}
