using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using System.Data;
using System.Data.SqlClient;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#401: la consulta del modelo de probabilidad de venta. No se puede ejecutar en un test
    /// (es SQL crudo contra la BD), pero sí fijar lo que la hacía peligrosa: el vendedor interpolado en
    /// el texto (inyectable) y el timeout por defecto de 30 s.
    /// </summary>
    [TestClass]
    public class GestorClientesObtenerClientesTests
    {
        [TestMethod]
        public void CrearComandoObtenerClientes_VendedorYTipoVanComoParametros_NuncaEnElTexto()
        {
            string vendedorMalicioso = "PA'; DROP TABLE Clientes; --";

            using (SqlCommand comando = GestorClientes.CrearComandoObtenerClientes(new SqlConnection(), vendedorMalicioso, "Visita"))
            {
                Assert.IsFalse(comando.CommandText.Contains("DROP TABLE"), "lo tecleado no llega al texto del SQL");
                Assert.AreEqual(vendedorMalicioso, comando.Parameters["@Vendedor"].Value);
                Assert.AreEqual(SqlDbType.VarChar, comando.Parameters["@Vendedor"].SqlDbType);
                Assert.AreEqual("Visita", comando.Parameters["@TipoInteraccion"].Value);
            }
        }

        [TestMethod]
        public void SqlObtenerClientes_NoInterpolaNada()
        {
            // Si alguien vuelve a meter $"...'{vendedor}'...", aparecerán llaves en el texto.
            Assert.IsFalse(GestorClientes.SQL_OBTENER_CLIENTES.Contains("{"));
            StringAssert.Contains(GestorClientes.SQL_OBTENER_CLIENTES, "vendedor = @Vendedor");
            StringAssert.Contains(GestorClientes.SQL_OBTENER_CLIENTES, "@TipoInteraccion AS TipoInteraccion");
        }

        [TestMethod]
        public void SqlObtenerClientes_MaterializaInteraccionesYPedidosFiltradosPorLosClientesDelVendedor()
        {
            // El motivo del timeout: CTEs de 5 años sin filtrar recalculadas en cuatro subconsultas por cliente.
            StringAssert.Contains(GestorClientes.SQL_OBTENER_CLIENTES, "INTO #Interacciones");
            StringAssert.Contains(GestorClientes.SQL_OBTENER_CLIENTES, "INTO #Pedidos");
            Assert.IsFalse(GestorClientes.SQL_OBTENER_CLIENTES.Contains("cte_pedidos"));
            Assert.IsFalse(GestorClientes.SQL_OBTENER_CLIENTES.Contains("cte_interacciones"));
        }

        [TestMethod]
        public void CrearComandoObtenerClientes_TimeoutDe120Segundos()
        {
            using (SqlCommand comando = GestorClientes.CrearComandoObtenerClientes(new SqlConnection(), "PA", null))
            {
                Assert.AreEqual(120, comando.CommandTimeout);
            }
        }

        [TestMethod]
        public void NormalizarTipoInteraccion_VacioOTelefono_EsLlamada()
        {
            Assert.AreEqual("Llamada", GestorClientes.NormalizarTipoInteraccion(null));
            Assert.AreEqual("Llamada", GestorClientes.NormalizarTipoInteraccion(""));
            Assert.AreEqual("Llamada", GestorClientes.NormalizarTipoInteraccion("Teléfono"));
            Assert.AreEqual("WhatsApp", GestorClientes.NormalizarTipoInteraccion("WhatsApp"));
        }
    }
}
