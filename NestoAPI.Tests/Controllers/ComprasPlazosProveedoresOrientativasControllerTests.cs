using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega;
using NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega.FechasOrientativas;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#861: GET api/Compras/PlazosProveedores/Orientativas y POST …/Orientativas/Procesar.</summary>
    [TestClass]
    public class ComprasPlazosProveedoresOrientativasControllerTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 13);
        private IRepositorioFechasOrientativas repositorio;
        private int encolados;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioFechasOrientativas>();
            A.CallTo(() => repositorio.TablaExiste()).Returns(true);
            A.CallTo(() => repositorio.LeerResueltas("1", A<DateTime>._)).Returns(new List<FilaErrorOrientativa>
            {
                new FilaErrorOrientativa { Proveedor = "65", YaTocabaPedir = true, ErrorPedidoDias = 2, ErrorLlegadaDias = 3 },
                new FilaErrorOrientativa { Proveedor = "12", YaTocabaPedir = false, ErrorPedidoDias = 0, ErrorLlegadaDias = -1 }
            });
            A.CallTo(() => repositorio.ContarPendientes("1", A<DateTime>._)).Returns((5, 1));
            encolados = 0;
        }

        private ComprasPlazosProveedoresController Controller(params string[] roles) =>
            new ComprasPlazosProveedoresController(() => new ServicioPlazosProveedores(A.Fake<IRepositorioPlazosProveedores>()), () => "0",
                crearOrientativas: () => new ServicioFechasOrientativas(repositorio, null, null, null, () => HOY),
                encolarOrientativas: () => { encolados++; return "88"; })
            {
                User = new GenericPrincipal(new GenericIdentity(@"NUEVAVISION\Prueba", "Bearer"), roles),
                Request = new HttpRequestMessage()
            };

        [TestMethod]
        public void GetOrientativas_SinGrupo_403()
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)Controller("Almacén").GetOrientativas()).StatusCode);
            A.CallTo(() => repositorio.LeerResueltas(A<string>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void GetOrientativas_DeCompras_ResumenPorProveedorYTotales()
        {
            var resultado = Controller("Compras").GetOrientativas(3) as OkNegotiatedContentResult<ResumenFechasOrientativasDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(3, resultado.Content.Meses);
            Assert.AreEqual(HOY.AddMonths(-3), resultado.Content.Desde);
            Assert.AreEqual(2, resultado.Content.Totales.Todas.Casos);
            Assert.AreEqual(50m, resultado.Content.Totales.Todas.PorcentajeCorta);
            Assert.AreEqual(2, resultado.Content.Proveedores.Count);
            Assert.AreEqual(5, resultado.Content.Abiertas);
            Assert.AreEqual(1, resultado.Content.Caducadas);
        }

        [TestMethod]
        public void GetOrientativas_SinTabla_503ConElScript()
        {
            A.CallTo(() => repositorio.TablaExiste()).Returns(false);

            var resultado = (NegotiatedContentResult<string>)Controller("Dirección").GetOrientativas();

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, resultado.StatusCode);
            StringAssert.Contains(resultado.Content, "Issue861_FechasOrientativasSinPedir.sql");
        }

        [TestMethod]
        public void ProcesarOrientativas_Compras_403YNoEncola()
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)Controller("Compras").PostProcesarOrientativas()).StatusCode);
            Assert.AreEqual(0, encolados);
        }

        [TestMethod]
        public void ProcesarOrientativas_Informatica_202Encolado()
        {
            var resultado = Controller("Informática").PostProcesarOrientativas() as NegotiatedContentResult<ProcesoFechasOrientativasDTO>;

            Assert.AreEqual(HttpStatusCode.Accepted, resultado.StatusCode);
            Assert.AreEqual("88", resultado.Content.JobId);
            Assert.AreEqual(1, encolados);
        }

        [TestMethod]
        public void ProcesarOrientativas_SinTabla_503YNoEncola()
        {
            A.CallTo(() => repositorio.TablaExiste()).Returns(false);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ((NegotiatedContentResult<string>)Controller("Dirección").PostProcesarOrientativas()).StatusCode);
            Assert.AreEqual(0, encolados);
        }
    }
}
