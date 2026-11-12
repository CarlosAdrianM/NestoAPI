using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#853: GET api/Compras/PlazosProveedores y POST …/Recalcular (permisos, sin tabla, encolado).</summary>
    [TestClass]
    public class ComprasPlazosProveedoresControllerTests
    {
        private IRepositorioPlazosProveedores repositorio;
        private int encolados;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioPlazosProveedores>();
            A.CallTo(() => repositorio.TablaPlazosExiste()).Returns(true);
            A.CallTo(() => repositorio.LeerPlazos("1")).Returns(new List<PlazoProveedor>
            {
                new PlazoProveedor { Empresa = "1", Proveedor = CalculoPlazosProveedores.PROVEEDOR_GENERICO, Recepciones = 2748, DiasMediana = 4, DiasP80 = 11 },
                new PlazoProveedor { Empresa = "1", Proveedor = "65", Recepciones = 252, DiasMediana = 2, DiasP80 = 4 }
            });
            encolados = 0;
        }

        private ComprasPlazosProveedoresController Controller(params string[] roles) =>
            new ComprasPlazosProveedoresController(() => new ServicioPlazosProveedores(repositorio), () => { encolados++; return "77"; })
            {
                User = new GenericPrincipal(new GenericIdentity(@"NUEVAVISION\Prueba", "Bearer"), roles),
                Request = new HttpRequestMessage()
            };

        [TestMethod]
        public void Get_SinGrupo_403()
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)Controller("Almacén").GetPlazos()).StatusCode);
        }

        [TestMethod]
        public void Get_DeCompras_LasFilasConLaGenericaAlFinal()
        {
            var resultado = Controller("Compras").GetPlazos() as OkNegotiatedContentResult<List<PlazoProveedor>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("65", resultado.Content[0].Proveedor);
            Assert.IsTrue(resultado.Content[1].EsGenerico);
        }

        [TestMethod]
        public void Get_SinTabla_503()
        {
            A.CallTo(() => repositorio.TablaPlazosExiste()).Returns(false);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ((NegotiatedContentResult<string>)Controller("Dirección").GetPlazos()).StatusCode);
        }

        [TestMethod]
        public void Recalcular_Compras_403YNoEncola()
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)Controller("Compras").PostRecalcular()).StatusCode);
            Assert.AreEqual(0, encolados);
        }

        [TestMethod]
        public void Recalcular_Informatica_202Encolado()
        {
            var resultado = Controller("Informática").PostRecalcular() as NegotiatedContentResult<RecalculoPlazosProveedoresDTO>;

            Assert.AreEqual(HttpStatusCode.Accepted, resultado.StatusCode);
            Assert.AreEqual("77", resultado.Content.JobId);
            Assert.AreEqual(1, encolados);
        }

        [TestMethod]
        public void Recalcular_SinTabla_503YNoEncola()
        {
            A.CallTo(() => repositorio.TablaPlazosExiste()).Returns(false);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ((NegotiatedContentResult<string>)Controller("Dirección").PostRecalcular()).StatusCode);
            Assert.AreEqual(0, encolados);
        }
    }
}
