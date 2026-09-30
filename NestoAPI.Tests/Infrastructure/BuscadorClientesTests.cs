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
        public void ElQueMasCompraNoSaleSiNoCoincideConLaBusqueda()
        {
            // Las ventas MULTIPLICAN la puntuación de texto, no la suman: si un cliente no casa
            // con nada, su puntuación es cero y cero por el factor que sea sigue siendo cero.
            // Por eso no hay que excluir a nadie del cálculo del ranking: el que más compra no
            // aparece en las búsquedas que no le tocan.
            List<string> encontrados = Buscar("PELUQUERIA").Select(r => r.Cliente).ToList();

            CollectionAssert.Contains(encontrados, "1519", "la peluquería sí tenía que salir");
            CollectionAssert.DoesNotContain(encontrados, "9471",
                "el cliente con el puesto 1 de ventas no coincide con 'PELUQUERIA' y no debe salir");
            CollectionAssert.DoesNotContain(encontrados, "22516",
                "el puesto 3 de ventas tampoco coincide y tampoco debe salir");
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

    /// <summary>
    /// NestoAPI#455: el buscador entra detrás de un flag, para poder encenderlo y apagarlo sin
    /// publicar. Mientras esté apagado, la búsqueda de clientes es exactamente la de siempre.
    /// </summary>
    [TestClass]
    public class BuscadorClientesFlagTests
    {
        [TestMethod]
        public void SoloSeActivaConTrue()
        {
            Assert.IsTrue(NestoAPI.Controllers.ClientesController.BuscadorPorIndiceActivo("true"));
            Assert.IsTrue(NestoAPI.Controllers.ClientesController.BuscadorPorIndiceActivo(" TRUE "));
            Assert.IsFalse(NestoAPI.Controllers.ClientesController.BuscadorPorIndiceActivo("false"));
            Assert.IsFalse(NestoAPI.Controllers.ClientesController.BuscadorPorIndiceActivo(""));
            Assert.IsFalse(NestoAPI.Controllers.ClientesController.BuscadorPorIndiceActivo(null));
            Assert.IsFalse(NestoAPI.Controllers.ClientesController.BuscadorPorIndiceActivo("1"),
                "cualquier cosa que no sea true deja la búsqueda de siempre");
        }
    }

    /// <summary>
    /// NestoAPI#566 (Lidia, 29/09/26): un cliente recién creado no salía hasta el día siguiente,
    /// ni buscando su número, porque el índice se reconstruye por las noches y, si devolvía
    /// cualquier otro cliente, ya no se miraba la base de datos.
    /// </summary>
    [TestClass]
    public class BuscadorClientesRecienCreadosTests
    {
        private static NestoAPI.Models.ClienteDTO Dto(string cliente, string contacto = "0")
        {
            return new NestoAPI.Models.ClienteDTO { cliente = cliente, contacto = contacto };
        }

        private static List<string> Numeros(IEnumerable<NestoAPI.Models.ClienteDTO> clientes)
        {
            return clientes.Select(c => c.cliente + "/" + c.contacto).ToList();
        }

        [TestMethod]
        public void Mezclar_ClienteNuevoQueElIndiceNoConoce_SaleDelanteDeLosDelIndice()
        {
            // El apellido lo comparten otros clientes ya indexados: el índice devuelve a esos
            var delIndice = new List<NestoAPI.Models.ClienteDTO> { Dto("100"), Dto("200") };
            var tocados = new List<NestoAPI.Models.ClienteDTO> { Dto("45001") };

            var resultado = NestoAPI.Controllers.ClientesController
                .MezclarConLosQueNoConoceElIndice(delIndice, tocados, "GARCIA");

            CollectionAssert.AreEqual(new[] { "45001/0", "100/0", "200/0" }, Numeros(resultado));
        }

        [TestMethod]
        public void Mezclar_NumeroExactoDeUnClienteNuevo_VaElPrimero()
        {
            // «45001» aparece en la dirección o el CP de otros clientes indexados
            var delIndice = new List<NestoAPI.Models.ClienteDTO> { Dto("100"), Dto("200") };
            var tocados = new List<NestoAPI.Models.ClienteDTO> { Dto("777"), Dto("45001") };

            var resultado = NestoAPI.Controllers.ClientesController
                .MezclarConLosQueNoConoceElIndice(delIndice, tocados, "45001");

            Assert.AreEqual("45001/0", Numeros(resultado).First());
            Assert.AreEqual(4, resultado.Count);
        }

        [TestMethod]
        public void Mezclar_ClienteModificadoHoyQueYaDevuelveElIndice_NoSeRepiteNiCambiaDeSitio()
        {
            var delIndice = new List<NestoAPI.Models.ClienteDTO> { Dto("100"), Dto("200"), Dto("300") };
            var tocados = new List<NestoAPI.Models.ClienteDTO> { Dto("300") };

            var resultado = NestoAPI.Controllers.ClientesController
                .MezclarConLosQueNoConoceElIndice(delIndice, tocados, "GARCIA");

            CollectionAssert.AreEqual(new[] { "100/0", "200/0", "300/0" }, Numeros(resultado));
        }

        [TestMethod]
        public void Mezclar_NumeroExactoDelIndiceConOtrosContactos_SiguenTodosDelante()
        {
            var delIndice = new List<NestoAPI.Models.ClienteDTO> { Dto("100"), Dto("15191", "0"), Dto("15191", "1") };

            var resultado = NestoAPI.Controllers.ClientesController
                .MezclarConLosQueNoConoceElIndice(delIndice, null, "15191");

            CollectionAssert.AreEqual(new[] { "15191/0", "15191/1", "100/0" }, Numeros(resultado));
        }

        [TestMethod]
        public void FechaConstruccion_ViajaEnElIndice()
        {
            string indice = Path.Combine(Path.GetTempPath(), "nesto_test_clientes_" + Guid.NewGuid().ToString("N"));
            try
            {
                DateTime inicio = new DateTime(2026, 9, 30, 2, 30, 0);
                BuscadorClientes.Indexar(indice, new List<ClienteIndexable>
                {
                    new ClienteIndexable { Empresa = "1", Cliente = "100", Contacto = "0", Nombre = "UNO" }
                }, inicio);

                Assert.AreEqual(inicio, BuscadorClientes.FechaConstruccionDe(indice));
            }
            finally
            {
                try { Directory.Delete(indice, true); } catch (IOException) { }
            }
        }

        [TestMethod]
        public void FechaConstruccion_SinIndice_EsNull()
        {
            string indice = Path.Combine(Path.GetTempPath(), "nesto_test_clientes_" + Guid.NewGuid().ToString("N"));
            try
            {
                Assert.IsNull(BuscadorClientes.FechaConstruccionDe(indice));
            }
            finally
            {
                try { Directory.Delete(indice, true); } catch (IOException) { }
            }
        }
    }
}
