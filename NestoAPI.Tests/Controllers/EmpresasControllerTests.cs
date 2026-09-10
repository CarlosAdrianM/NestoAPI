using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#472: GET api/Empresas devolvía la entidad de EF entera y Nesto no podía
    /// deserializarla (allí Vendedores es un objeto, aquí una colección). Agencias se quedaba
    /// sin empresas y la etiqueta de ASM/CEX dejaba de salir, en silencio.
    /// </summary>
    [TestClass]
    public class EmpresasControllerTests
    {
        private NVEntities db;
        private DbSet<Empresa> fakeEmpresas;
        private EmpresasController controller;

        // Réplica mínima de cómo ve la respuesta Nesto (Nesto.Models/Empresas.vb): Vendedores es
        // un OBJETO. Si el servidor manda un array, Newtonsoft lanza JsonSerializationException
        // en "[0].Vendedores" — exactamente el error de ELMAH del 09 y 10/09/26.
        private class EmpresaComoLaVeNesto
        {
            public string Número { get; set; }
            public string Nombre { get; set; }
            public string FormaPagoEfectivo { get; set; }
            public VendedorComoLoVeNesto Vendedores { get; set; }
        }

        private class VendedorComoLoVeNesto
        {
            public string Número { get; set; }
        }

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeEmpresas = A.Fake<DbSet<Empresa>>(o => o
                .Implements<IQueryable<Empresa>>()
                .Implements<IDbAsyncEnumerable<Empresa>>());
            A.CallTo(() => db.Empresas).Returns(fakeEmpresas);
            controller = new EmpresasController(db);
        }

        private void Datos(params Empresa[] empresas)
        {
            ConfigurarFakeDbSet(fakeEmpresas, empresas.AsQueryable());
        }

        private static Empresa EmpresaUno()
        {
            return new Empresa
            {
                Número = "1  ",
                Nombre = "NUEVA VISION SA",
                FormaPagoEfectivo = "EFC",
                FormaVentaVarios = "VAR",
                DelegaciónVarios = "ALG"
            };
        }

        [TestMethod]
        public void GetEmpresas_LaRespuestaSeDeserializaConElModeloDeNesto()
        {
            Datos(EmpresaUno(), new Empresa { Número = "3  ", Nombre = "OTRA" });

            var resultado = controller.GetEmpresas() as OkNegotiatedContentResult<List<EmpresaDTO>>;

            Assert.IsNotNull(resultado);
            string json = JsonConvert.SerializeObject(resultado.Content);
            // Antes del arreglo esta línea lanzaba JsonSerializationException.
            var comoNesto = JsonConvert.DeserializeObject<List<EmpresaComoLaVeNesto>>(json);
            Assert.AreEqual(2, comoNesto.Count);
            Assert.AreEqual("1  ", comoNesto[0].Número, "el relleno del char se conserva: los llamantes ya recortan o comparan con CampoIgual");
            Assert.AreEqual("NUEVA VISION SA", comoNesto[0].Nombre);
            Assert.AreEqual("EFC", comoNesto[0].FormaPagoEfectivo, "Agencias lee FormaPagoEfectivo de la empresa");
            Assert.IsNull(comoNesto[0].Vendedores);
        }

        [TestMethod]
        public void GetEmpresas_LaEntidadDeEF_SiQueRevientaEnNesto()
        {
            // Documenta el bug: lo que devolvía el endpoint antes era la entidad, y la entidad lleva
            // "Vendedores":[] aunque no se cargue nada. Un [] tampoco cabe en una propiedad escalar.
            string json = JsonConvert.SerializeObject(new List<Empresa> { EmpresaUno() });

            StringAssert.Contains(json, "\"Vendedores\":[]");
            Assert.ThrowsException<JsonSerializationException>(
                () => JsonConvert.DeserializeObject<List<EmpresaComoLaVeNesto>>(json));
        }

        [TestMethod]
        public void EmpresaDTO_NoExponeNingunaNavegacionDeEF()
        {
            var sospechosas = typeof(EmpresaDTO).GetProperties()
                .Where(p => p.PropertyType.Namespace == typeof(Empresa).Namespace
                         || (typeof(System.Collections.IEnumerable).IsAssignableFrom(p.PropertyType) && p.PropertyType != typeof(string)))
                .Select(p => p.Name)
                .ToList();

            Assert.AreEqual(0, sospechosas.Count, "navegaciones coladas en el DTO: " + string.Join(", ", sospechosas));
        }

        [TestMethod]
        public void EmpresaDTO_ConservaTodasLasColumnasDeLaTabla()
        {
            // El DTO existe para quitar navegaciones, NO columnas: los llamantes de Nesto leen
            // campos sueltos y ninguno debe notar el cambio. Las navegaciones de EF son virtual;
            // las columnas, no.
            var columnas = typeof(Empresa).GetProperties()
                .Where(p => !p.GetGetMethod().IsVirtual)
                .Select(p => p.Name).OrderBy(n => n).ToList();
            var dto = typeof(EmpresaDTO).GetProperties()
                .Select(p => p.Name).OrderBy(n => n).ToList();

            CollectionAssert.AreEqual(columnas, dto);
        }

        [TestMethod]
        public async Task GetEmpresa_PorNumero_DevuelveElDTO()
        {
            A.CallTo(() => fakeEmpresas.FindAsync(A<object[]>.That.Matches(k => (string)k[0] == "1  ")))
                .Returns(Task.FromResult(EmpresaUno()));

            var resultado = await controller.GetEmpresa("1  ") as OkNegotiatedContentResult<EmpresaDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("NUEVA VISION SA", resultado.Content.Nombre);
            Assert.AreEqual("ALG", resultado.Content.DelegaciónVarios);
        }

        [TestMethod]
        public async Task GetEmpresa_SiNoExiste_NotFound()
        {
            A.CallTo(() => fakeEmpresas.FindAsync(A<object[]>.Ignored))
                .Returns(Task.FromResult<Empresa>(null));

            Assert.IsInstanceOfType(await controller.GetEmpresa("9  "), typeof(NotFoundResult));
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).Returns(data.GetEnumerator());
        }
    }
}
