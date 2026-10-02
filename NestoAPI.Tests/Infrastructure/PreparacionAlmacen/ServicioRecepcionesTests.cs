using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    // NestoAPI#559/#553: el núcleo de «Recibir» es el mismo para todos los tipos; cada tipo es una estrategia.
    [TestClass]
    public class ServicioRecepcionesTests
    {
        private IOrigenRecepcion compras;
        private IOrigenRecepcion reposiciones;
        private ServicioRecepciones servicio;
        private IPrincipal usuario;

        [TestInitialize]
        public void Inicializar()
        {
            compras = A.Fake<IOrigenRecepcion>();
            A.CallTo(() => compras.Tipo).Returns("COMP");
            A.CallTo(() => compras.SeTerminaDesdeAqui).Returns(true);
            reposiciones = A.Fake<IOrigenRecepcion>();
            A.CallTo(() => reposiciones.Tipo).Returns("REPO");
            servicio = new ServicioRecepciones(new[] { compras, reposiciones });
            usuario = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Andre") }, "prueba"));
        }

        private static RecepcionDTO Esperado(params (string producto, int cantidad)[] lineas)
        {
            return new RecepcionDTO
            {
                Tipo = "COMP",
                Documento = "65",
                Lineas = lineas.Select(l => new LineaRecepcionDTO { Producto = l.producto, Descripcion = "d" + l.producto, Cantidad = l.cantidad }).ToList()
            };
        }

        private static TerminarRecepcionDTO Terminar(params (string producto, int cantidad)[] lecturas)
        {
            return new TerminarRecepcionDTO
            {
                IdRecepcion = Guid.NewGuid(),
                Lecturas = lecturas.Select(l => new LecturaRecepcionDTO { Producto = l.producto, Cantidad = l.cantidad }).ToList()
            };
        }

        [TestMethod]
        public async Task LeerPendientes_JuntaLosDeTodosLosTipos()
        {
            A.CallTo(() => compras.LeerPendientes("1", "ALG")).Returns(new List<RecepcionPendienteDTO> { new RecepcionPendienteDTO { Tipo = "COMP", Documento = "65" } });
            A.CallTo(() => reposiciones.LeerPendientes("1", "ALG")).Returns(new List<RecepcionPendienteDTO> { new RecepcionPendienteDTO { Tipo = "REPO", Documento = "80841" } });

            List<RecepcionPendienteDTO> pendientes = await servicio.LeerPendientes("1", "ALG");

            CollectionAssert.AreEquivalent(new[] { "COMP", "REPO" }, pendientes.Select(p => p.Tipo).ToList());
        }

        [TestMethod]
        public async Task LeerEsperado_DiceSiQuienPreguntaPuedeTerminar()
        {
            A.CallTo(() => compras.LeerEsperado("1", "ALG", "65")).Returns(Esperado(("A", 2)));
            A.CallTo(() => compras.PuedeTerminar(usuario, A<string>.Ignored, A<string>.Ignored)).Returns(true);

            RecepcionDTO recepcion = await servicio.LeerEsperado("comp", "1", "ALG", "65", usuario);

            Assert.IsTrue(recepcion.PuedeTerminar);
            Assert.IsTrue(recepcion.SeTerminaDesdeAqui);
        }

        [TestMethod]
        public async Task Casar_EsComunParaTodosLosTipos()
        {
            A.CallTo(() => reposiciones.LeerEsperado("1", "REI", "80841")).Returns(Esperado(("A", 2), ("B", 1)));

            ResultadoCasarRecepcionDTO resultado = await servicio.Casar("REPO", "1", "REI", "80841",
                new List<LecturaRecepcionDTO> { new LecturaRecepcionDTO { Producto = "a", Cantidad = 2 }, new LecturaRecepcionDTO { Producto = "Z", Cantidad = 1 } });

            Assert.IsFalse(resultado.Cuadra);
            Assert.AreEqual(-1, resultado.Productos.Single(p => p.Producto == "B").Diferencia);
            Assert.IsTrue(resultado.Productos.Single(p => p.Producto == "Z").Ajeno);
        }

        [TestMethod]
        public async Task Buscar_JuntaLoQueEncuentraCadaTipo()
        {
            A.CallTo(() => compras.BuscarPorCodigo("1", "ALG", "8436620930427"))
                .Returns(new List<RecepcionPendienteDTO> { new RecepcionPendienteDTO { Tipo = "COMP", Documento = "65" } });
            A.CallTo(() => reposiciones.BuscarPorCodigo("1", "ALG", "8436620930427")).Returns(new List<RecepcionPendienteDTO>());

            List<RecepcionPendienteDTO> encontradas = await servicio.Buscar("1", "ALG", " 8436620930427 ");

            Assert.AreEqual("65", encontradas.Single().Documento);
        }

        [TestMethod]
        public async Task TipoDesconocido_ErrorDeNegocio()
        {
            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                servicio.Terminar("XXXX", "1", "ALG", "65", Terminar(("A", 1)), usuario));

            StringAssert.Contains(ex.Message, "XXXX");
        }

        [TestMethod]
        public async Task SinPermisoParaEseTipo_NoSeTermina()
        {
            A.CallTo(() => compras.PuedeTerminar(usuario, A<string>.Ignored, A<string>.Ignored)).Returns(false);

            _ = await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() =>
                servicio.Terminar("COMP", "1", "ALG", "65", Terminar(("A", 1)), usuario));
            A.CallTo(() => compras.Terminar(A<SolicitudTerminarRecepcion>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task TipoQueTodaviaNoSeTerminaDesdeAqui_LoDiceYNoHaceNada()
        {
            A.CallTo(() => reposiciones.PuedeTerminar(usuario, A<string>.Ignored, A<string>.Ignored)).Returns(true);
            A.CallTo(() => reposiciones.SeTerminaDesdeAqui).Returns(false);

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                servicio.Terminar("REPO", "1", "REI", "80841", Terminar(("A", 1)), usuario));

            StringAssert.Contains(ex.Message, "todavía");
            A.CallTo(() => reposiciones.Terminar(A<SolicitudTerminarRecepcion>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SinIdDeRecepcion_NoSeTermina()
        {
            A.CallTo(() => compras.PuedeTerminar(usuario, A<string>.Ignored, A<string>.Ignored)).Returns(true);
            TerminarRecepcionDTO terminar = Terminar(("A", 1));
            terminar.IdRecepcion = Guid.Empty;

            _ = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.Terminar("COMP", "1", "ALG", "65", terminar, usuario));
        }

        [TestMethod]
        public async Task SinNadaLeido_NoSeTermina()
        {
            A.CallTo(() => compras.PuedeTerminar(usuario, A<string>.Ignored, A<string>.Ignored)).Returns(true);

            _ = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                servicio.Terminar("COMP", "1", "ALG", "65", Terminar(("A", 0)), usuario));
        }

        [TestMethod]
        public async Task SinUsuarioIdentificado_NoSeTermina_NuncaSeInventa()
        {
            var anonimo = new ClaimsPrincipal(new ClaimsIdentity());
            A.CallTo(() => compras.PuedeTerminar(anonimo, A<string>.Ignored, A<string>.Ignored)).Returns(true);

            _ = await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() =>
                servicio.Terminar("COMP", "1", "ALG", "65", Terminar(("A", 1)), anonimo));
        }

        [TestMethod]
        public async Task Terminar_PasaLoLeidoSumadoPorProductoYElUsuarioDelToken()
        {
            A.CallTo(() => compras.PuedeTerminar(usuario, A<string>.Ignored, A<string>.Ignored)).Returns(true);
            SolicitudTerminarRecepcion recibida = null;
            A.CallTo(() => compras.Terminar(A<SolicitudTerminarRecepcion>.Ignored))
                .Invokes((SolicitudTerminarRecepcion s) => recibida = s)
                .Returns(new ResultadoTerminarRecepcionDTO());

            _ = await servicio.Terminar("COMP", " 1 ", "alg", " 65 ", Terminar(("A", 2), (" a ", 3), ("B", 1)), usuario);

            Assert.AreEqual(5, recibida.Lecturas["A"]);
            Assert.AreEqual(1, recibida.Lecturas["B"]);
            Assert.AreEqual("Andre", recibida.Usuario);
            Assert.AreEqual("1", recibida.Empresa);
            Assert.AreEqual("ALG", recibida.Almacen);
            Assert.AreEqual("65", recibida.Documento);
        }
    }
}
