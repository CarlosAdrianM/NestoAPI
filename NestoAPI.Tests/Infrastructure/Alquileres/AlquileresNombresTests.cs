using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Alquileres;
using NestoAPI.Models;
using NestoAPI.Models.Alquileres;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Alquileres
{
    /// <summary>
    /// Novedad 1181 (Aida): la relación de alquileres trae el nombre del equipo (ProductoNombre, de la ficha del producto) y el del
    /// cliente (ClienteNombre, del contacto o, si no casa, del principal), tanto por producto como por cliente.
    /// </summary>
    [TestClass]
    public class AlquileresNombresTests
    {
        private List<CabAlquiler> cabeceras;
        private List<Cliente> clientes;
        private List<Producto> productos;
        private NVEntities db;

        [TestInitialize]
        public void Inicializar()
        {
            cabeceras = new List<CabAlquiler>
            {
                Cabecera(1, "15191", "0", "32242", "S-1", nombreProductoCopia: "CAVITACION VIEJA"),
                Cabecera(2, "15191", null, "40100", "S-2", nombreProductoCopia: "COPIA RADIOFRECUENCIA"),
                Cabecera(3, "20000", "1", "32242", "S-3")
            };
            clientes = new List<Cliente>
            {
                new Cliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "0  ", Nombre = "PELUQUERÍA EJEMPLO   ", ClientePrincipal = true },
                new Cliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "1  ", Nombre = "OTRO CENTRO", ClientePrincipal = false },
                new Cliente { Empresa = "1", Nº_Cliente = "20000", Contacto = "1  ", Nombre = "ESTÉTICA SEGUNDA  ", ClientePrincipal = false }
            };
            productos = new List<Producto>
            {
                new Producto { Empresa = "1", Número = "32242", Nombre = "CAVITACION ODÍN   " }
            };
            db = A.Fake<NVEntities>();
            A.CallTo(() => db.CabAlquileres).Returns(FakeDbSet(cabeceras));
            A.CallTo(() => db.Clientes).Returns(FakeDbSet(clientes));
            A.CallTo(() => db.Productos).Returns(FakeDbSet(productos));
        }

        private static CabAlquiler Cabecera(int numero, string cliente, string contacto, string producto, string serie, string nombreProductoCopia = null) =>
            new CabAlquiler
            {
                Empresa = "1",
                Número = numero,
                // En memoria la comparación no ignora los espacios finales (SQL Server sí): las claves van sin rellenar.
                Cliente = cliente,
                Contacto = contacto == null ? null : contacto.PadRight(3),
                Producto = producto,
                NumeroSerie = serie,
                NombreProducto = nombreProductoCopia,
                Usuario = "prueba",
                FechaModificación = new DateTime(2026, 11, 12)
            };

        private static DbSet<T> FakeDbSet<T>(List<T> datos) where T : class
        {
            DbSet<T> fakeSet = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IQueryable<T>)fakeSet).Provider).ReturnsLazily(() => new TestDbAsyncQueryProvider<T>(datos.AsQueryable().Provider));
            A.CallTo(() => ((IQueryable<T>)fakeSet).Expression).ReturnsLazily(() => datos.AsQueryable().Expression);
            A.CallTo(() => ((IQueryable<T>)fakeSet).ElementType).Returns(typeof(T));
            A.CallTo(() => ((IQueryable<T>)fakeSet).GetEnumerator()).ReturnsLazily(() => datos.GetEnumerator());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeSet).GetAsyncEnumerator()).ReturnsLazily(() => new TestDbAsyncEnumerator<T>(datos.GetEnumerator()));
            return fakeSet;
        }

        private AlquileresController Controlador() => new AlquileresController(new ProductosAlquilerService(db));

        [TestMethod]
        public async Task PorCliente_TraeElNombreDelEquipoYElDelCliente()
        {
            var resultado = await Controlador().GetCabecerasAlquilerPorCliente("1", " 15191 ")
                as OkNegotiatedContentResult<List<AlquilerCabeceraDTO>>;

            Assert.IsNotNull(resultado);
            List<AlquilerCabeceraDTO> lista = resultado.Content;
            Assert.AreEqual(2, lista.Count);
            AlquilerCabeceraDTO odin = lista.Single(c => c.Numero == 1);
            Assert.AreEqual("32242", odin.Producto);
            Assert.AreEqual("CAVITACION ODÍN", odin.ProductoNombre);
            Assert.AreEqual("PELUQUERÍA EJEMPLO", odin.ClienteNombre);
            Assert.AreEqual("PELUQUERÍA EJEMPLO", odin.NombreCliente);
        }

        [TestMethod]
        public async Task PorCliente_SinContacto_NombreDelPrincipal_YSinFichaDeProducto_LaCopiaDeLaCabecera()
        {
            var resultado = await Controlador().GetCabecerasAlquilerPorCliente("1", "15191")
                as OkNegotiatedContentResult<List<AlquilerCabeceraDTO>>;

            AlquilerCabeceraDTO sinContacto = resultado.Content.Single(c => c.Numero == 2);
            Assert.AreEqual("PELUQUERÍA EJEMPLO", sinContacto.ClienteNombre);
            Assert.IsNull(sinContacto.NombreCliente, "NombreCliente (etiqueta) sigue siendo solo el del contacto exacto");
            Assert.AreEqual("COPIA RADIOFRECUENCIA", sinContacto.ProductoNombre);
        }

        [TestMethod]
        public async Task PorProducto_TraeElNombreDeCadaCliente()
        {
            var resultado = await Controlador().GetCabecerasAlquiler("1", "32242")
                as OkNegotiatedContentResult<List<AlquilerCabeceraDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(2, resultado.Content.Count);
            Assert.AreEqual("PELUQUERÍA EJEMPLO", resultado.Content.Single(c => c.Numero == 1).ClienteNombre);
            Assert.AreEqual("ESTÉTICA SEGUNDA", resultado.Content.Single(c => c.Numero == 3).ClienteNombre);
            Assert.IsTrue(resultado.Content.All(c => c.ProductoNombre == "CAVITACION ODÍN"));
        }
    }
}
