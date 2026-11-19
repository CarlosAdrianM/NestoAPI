using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>
    /// NestoAPI#879 (novedad 1320): la «dirección completa» que se enseña (ficha, selector de clientes, cabecera del pedido) =
    /// Dirección + «, » + Dirección 2, sin truncar. Una consulta por lista, nunca una por fila.
    /// </summary>
    [TestClass]
    public class DireccionCompletaClienteTests
    {
        [TestCleanup]
        public void Limpiar()
        {
            Direccion2ClienteSql.LectorClientesParaTests = null;
            Direccion2ClienteSql.LectorParaTests = null;
        }

        // ---------------------------------------------------------------- regla pura

        [TestMethod]
        public void Componer_ConDireccion2_SeparadaPorComa()
        {
            Assert.AreEqual("CALLE MAYOR, 3, LOCAL · RÓTULO: LOHÉ ESTUDIO",
                DireccionCompletaCliente.Componer("CALLE MAYOR, 3   ", " LOCAL · RÓTULO: LOHÉ ESTUDIO "));
        }

        [TestMethod]
        public void Componer_SinDireccion2_LaDireccionTalCual()
        {
            Assert.AreEqual("CALLE MAYOR, 3", DireccionCompletaCliente.Componer("CALLE MAYOR, 3  ", null));
            Assert.AreEqual("CALLE MAYOR, 3", DireccionCompletaCliente.Componer("CALLE MAYOR, 3", "   "));
        }

        [TestMethod]
        public void Componer_SinDireccion_LaDireccion2YNullSinNada()
        {
            Assert.AreEqual("CASA", DireccionCompletaCliente.Componer("  ", "CASA"));
            Assert.IsNull(DireccionCompletaCliente.Componer(null, null));
        }

        [TestMethod]
        public void Componer_NoTrunca_AunqueNoQuepaEnLaEtiquetaDeLaAgencia()
        {
            string calle = new string('A', 50);
            string d2 = new string('B', 100);
            string completa = DireccionCompletaCliente.Componer(calle, d2);
            Assert.AreEqual(152, completa.Length, "Es para pantalla: no tiene el tope de 80 de la etiqueta");
            // La de la etiqueta sigue siendo otra cosa: con 152 no cabe y se va a Observaciones
            Assert.AreEqual(calle, DireccionEnvioCliente.Componer(calle, d2, null).Direccion);
        }

        // ---------------------------------------------------------------- listas: una consulta

        private static Dictionary<string, Direccion2Contacto> Filas(params (string cliente, string contacto, string d2)[] filas) =>
            filas.ToDictionary(f => Direccion2ClienteSql.ClaveContacto(f.cliente, f.contacto),
                f => new Direccion2Contacto { Cliente = f.cliente, Contacto = f.contacto, Direccion2 = f.d2 });

        [TestMethod]
        public void RellenarLista_UnaConsultaPorEmpresa_YCadaContactoConLaSuya()
        {
            var llamadas = new List<(string empresa, IReadOnlyList<string> clientes)>();
            Direccion2ClienteSql.LectorClientesParaTests = (empresa, clientes) =>
            {
                llamadas.Add((empresa, clientes));
                return Filas(("42143", "0", "LOCAL · RÓTULO: LOHÉ ESTUDIO"), ("42143", "1", null));
            };
            var lista = new List<ClienteDTO>
            {
                new ClienteDTO { empresa = "1", cliente = "42143", contacto = "0", direccion = "CALLE MAYOR, 3" },
                new ClienteDTO { empresa = "1", cliente = "42143", contacto = "1", direccion = "CALLE MENOR, 5" },
                new ClienteDTO { empresa = "1", cliente = "15191", contacto = "0", direccion = "PLAZA, 1" }
            };

            DireccionCompletaCliente.Rellenar(null, lista);

            Assert.AreEqual(1, llamadas.Count, "Una sola consulta para toda la lista");
            CollectionAssert.AreEquivalent(new[] { "42143", "15191" }, llamadas[0].clientes.ToList(), "Cada cliente una vez");
            Assert.AreEqual("CALLE MAYOR, 3, LOCAL · RÓTULO: LOHÉ ESTUDIO", lista[0].direccionCompleta);
            Assert.AreEqual("CALLE MENOR, 5", lista[1].direccionCompleta);
            Assert.AreEqual("PLAZA, 1", lista[2].direccionCompleta, "Sin fila: la dirección tal cual");
            Assert.AreEqual("CALLE MAYOR, 3", lista[0].direccion, "direccion no cambia (NestoApp y la tienda la usan)");
        }

        [TestMethod]
        public void RellenarLista_DosEmpresas_UnaConsultaPorEmpresa()
        {
            var empresas = new List<string>();
            Direccion2ClienteSql.LectorClientesParaTests = (empresa, clientes) => { empresas.Add(empresa); return Filas(); };
            var lista = new List<ClienteDTO>
            {
                new ClienteDTO { empresa = "1", cliente = "1", contacto = "0", direccion = "A" },
                new ClienteDTO { empresa = "3", cliente = "1", contacto = "0", direccion = "B" },
                new ClienteDTO { empresa = "1", cliente = "2", contacto = "0", direccion = "C" }
            };
            DireccionCompletaCliente.Rellenar(null, lista);
            CollectionAssert.AreEquivalent(new[] { "1", "3" }, empresas);
        }

        [TestMethod]
        public void RellenarLista_SinColumnasOFalla_LaDireccionTalCual()
        {
            Direccion2ClienteSql.LectorClientesParaTests = (empresa, clientes) => null; // sin el script o error de lectura
            var lista = new List<ClienteDTO> { new ClienteDTO { empresa = "1", cliente = "42143", contacto = "0", direccion = "CALLE MAYOR, 3  " } };
            DireccionCompletaCliente.Rellenar(null, lista);
            Assert.AreEqual("CALLE MAYOR, 3", lista[0].direccionCompleta);
        }

        [TestMethod]
        public void RellenarLista_ListaVaciaONull_NoConsulta()
        {
            int llamadas = 0;
            Direccion2ClienteSql.LectorClientesParaTests = (empresa, clientes) => { llamadas++; return null; };
            DireccionCompletaCliente.Rellenar(null, new List<ClienteDTO>());
            DireccionCompletaCliente.Rellenar(null, (IList<ClienteDTO>)null);
            Assert.AreEqual(0, llamadas);
        }

        [TestMethod]
        public void RellenarDireccionesEntrega_UnaConsultaYMarcaCadaContacto()
        {
            int llamadas = 0;
            Direccion2ClienteSql.LectorClientesParaTests = (empresa, clientes) =>
            {
                llamadas++;
                Assert.AreEqual("42143", clientes.Single());
                return Filas(("42143", "1", "1º D"));
            };
            var direcciones = new List<DireccionesEntregaClienteDTO>
            {
                new DireccionesEntregaClienteDTO { contacto = "0", direccion = "CALLE MAYOR, 3" },
                new DireccionesEntregaClienteDTO { contacto = "1", direccion = "CALLE MENOR, 5" }
            };
            DireccionCompletaCliente.Rellenar(null, "1", "42143 ", direcciones);
            Assert.AreEqual(1, llamadas);
            Assert.AreEqual("CALLE MAYOR, 3", direcciones[0].direccionCompleta);
            Assert.AreEqual("CALLE MENOR, 5, 1º D", direcciones[1].direccionCompleta);
        }

        [TestMethod]
        public void RellenarUno_LeeLaDeEseContacto()
        {
            Direccion2ClienteSql.LectorParaTests = (empresa, cliente, contacto) => cliente == "42143" && contacto == "2" ? "NAVE 4" : null;
            var cliente1 = new ClienteDTO { empresa = "1", cliente = "42143", contacto = "2", direccion = "POLÍGONO, 7" };
            DireccionCompletaCliente.Rellenar(null, cliente1);
            Assert.AreEqual("POLÍGONO, 7, NAVE 4", cliente1.direccionCompleta);

            var cliente2 = new ClienteDTO { empresa = "1", cliente = "42143", contacto = "0", direccion = "POLÍGONO, 7" };
            DireccionCompletaCliente.Rellenar(null, cliente2);
            Assert.AreEqual("POLÍGONO, 7", cliente2.direccionCompleta);
        }

        [TestMethod]
        public void LeerDeClientes_SinBaseDeDatos_NullSinLanzar()
        {
            Assert.IsNull(Direccion2ClienteSql.LeerDeClientes(null, "1", new[] { "42143" }, Direccion2ClienteSql.PARA_PANTALLA));
        }
    }
}
