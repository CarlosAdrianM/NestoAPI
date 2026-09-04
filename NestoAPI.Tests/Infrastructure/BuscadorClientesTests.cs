using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Buscador;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#455: buscador de clientes. Se indexa en una carpeta temporal y se busca de verdad,
    /// que es la única forma de comprobar el orden: los pesos no se pueden razonar sobre el papel.
    /// </summary>
    [TestClass]
    public class BuscadorClientesTests
    {
        private string _indice;

        [TestInitialize]
        public void Preparar()
        {
            _indice = Path.Combine(Path.GetTempPath(), "nesto_test_clientes_" + Guid.NewGuid().ToString("N"));
            BuscadorClientes.Indexar(_indice, Clientes());
        }

        [TestCleanup]
        public void Limpiar()
        {
            try
            {
                if (Directory.Exists(_indice))
                {
                    Directory.Delete(_indice, true);
                }
            }
            catch (IOException)
            {
                // Un índice temporal que no se deja borrar no puede tumbar la suite
            }
        }

        private static ClienteIndexable Cliente(string numero, string nombre, string direccion,
            string cp, string poblacion, int puestoVentas)
        {
            return new ClienteIndexable
            {
                Empresa = "1",
                Cliente = numero,
                Contacto = "0",
                Nombre = nombre,
                Direccion = direccion,
                CodigoPostal = cp,
                Poblacion = poblacion,
                PosicionVentas = puestoVentas
            };
        }

        private static List<ClienteIndexable> Clientes()
        {
            return new List<ClienteIndexable>
            {
                Cliente("15191", "CARLOS ADRIAN MARTINEZ", "CALLE RIO TIETAR 11", "28119", "ALGETE", 40),
                Cliente("1519", "PELUQUERIA ROSA", "AVENIDA DE LA PAZ 3", "28001", "MADRID", 900),
                Cliente("22516", "CARLOS SANCHEZ PEREZ", "CALLE MAYOR 5", "28013", "MADRID", 3),
                Cliente("41266", "CARLOS GOMEZ RUIZ", "PLAZA ESPANA 2", "29017", "MALAGA", 1500),
                Cliente("9471", "RAQUEL YUSTA CATALINA", "CALLE TIETAR 8", "28119", "ALGETE", 1),
                new ClienteIndexable
                {
                    Empresa = "3",
                    Cliente = "70001",
                    Contacto = "0",
                    Nombre = "CARLOS DE OTRA EMPRESA",
                    Direccion = "CALLE FALSA 1",
                    CodigoPostal = "28001",
                    Poblacion = "MADRID",
                    PosicionVentas = 2
                }
            };
        }

        private List<ClaveCliente> Buscar(string texto, string empresa = "1")
        {
            return BuscadorClientes.BuscarEnIndice(_indice, empresa, texto, 20);
        }

        [TestMethod]
        public void ElNumeroExactoSaleElPrimeroYElParecidoNoSale()
        {
            // "si busco 15191 el cliente 15191 tiene que salir el primero, pero el 1519 no pinta
            // nada ahi" (texto literal de la issue)
            List<ClaveCliente> resultados = Buscar("15191");

            Assert.IsTrue(resultados.Any(), "No ha encontrado nada");
            Assert.AreEqual("15191", resultados.First().Cliente);
            Assert.IsFalse(resultados.Any(r => r.Cliente == "1519"),
                "El 1519 no debe aparecer al buscar 15191");
        }

        [TestMethod]
        public void BuscandoUnNombre_SalenTodosOrdenadosDelQueMasCompraAlQueMenos()
        {
            // El ejemplo de la issue: "si busco por Carlos me mostrara todos los Carlos ordenados
            // del que mas compra al que menos"
            List<string> carlos = Buscar("CARLOS").Select(r => r.Cliente).ToList();

            CollectionAssert.AreEqual(
                new List<string> { "22516", "15191", "41266" },
                carlos,
                "Esperado 22516 (puesto 3), 15191 (puesto 40) y 41266 (puesto 1500); salió: " +
                    string.Join(", ", carlos));
        }

        [TestMethod]
        public void SoloDevuelveClientesDeLaEmpresaPedida()
        {
            Assert.IsFalse(Buscar("CARLOS").Any(r => r.Cliente == "70001"));
            Assert.IsTrue(Buscar("CARLOS", empresa: "3").Any(r => r.Cliente == "70001"));
        }

        [TestMethod]
        public void EncuentraPorDireccionYPorPoblacion()
        {
            Assert.IsTrue(Buscar("TIETAR").Any(r => r.Cliente == "15191"), "por dirección");
            Assert.IsTrue(Buscar("MALAGA").Any(r => r.Cliente == "41266"), "por población");
        }

        [TestMethod]
        public void EncuentraPorCodigoPostal()
        {
            List<string> encontrados = Buscar("28119").Select(r => r.Cliente).ToList();

            CollectionAssert.Contains(encontrados, "15191");
            CollectionAssert.Contains(encontrados, "9471");
        }

        [TestMethod]
        public void ConUnaErrata_LoEncuentraIgual()
        {
            // El rescate fonético/difuso: "Karlos" no existe escrito así en ningún cliente
            List<string> encontrados = Buscar("KARLOS").Select(r => r.Cliente).ToList();

            Assert.IsTrue(encontrados.Any(), "El rescate por erratas no ha encontrado nada");
            CollectionAssert.Contains(encontrados, "22516");
        }

        [TestMethod]
        public void BusquedaVacia_NoFallaYNoDevuelveNada()
        {
            Assert.AreEqual(0, Buscar("").Count);
            Assert.AreEqual(0, Buscar("   ").Count);
            Assert.AreEqual(0, Buscar(null).Count);
        }

        [TestMethod]
        public void FactorVentas_ElQueMasCompraPuntuaMasYElQueNoCompraNoPenaliza()
        {
            float primero = BuscadorClientes.FactorVentas(1);
            float intermedio = BuscadorClientes.FactorVentas(500);
            float sinVentas = BuscadorClientes.FactorVentas(0);

            Assert.IsTrue(primero > intermedio, "el primero tiene que puntuar más que el 500");
            Assert.IsTrue(intermedio > 1f, "el 500 sigue sumando algo");
            Assert.AreEqual(1f, sinVentas, "quien no compra no puede salir penalizado, solo no sube");
            Assert.AreEqual(1f, BuscadorClientes.FactorVentas(99999), "pasado el horizonte, no suma");
        }
    }
}
