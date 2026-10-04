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
        public async Task Casar_LoQueSobraYSeDioPorNoServidoHacePoco_SeDiceConSuPedido()
        {
            RecepcionDTO esperado = Esperado(("A", 2));
            esperado.Lineas[0].Recuperables.Add(new RecuperableRecepcionDTO { Pedido = 220396, Cantidad = 3, FechaNoServido = new DateTime(2026, 9, 28) });
            A.CallTo(() => compras.LeerEsperado("1", "ALG", "65")).Returns(esperado);

            ResultadoCasarRecepcionDTO resultado = await servicio.Casar("COMP", "1", "ALG", "65",
                new List<LecturaRecepcionDTO> { new LecturaRecepcionDTO { Producto = "A", Cantidad = 4 } });

            StringAssert.Contains(resultado.Recuperadas.Single(), "2 ud. de A eran del pedido 220396");
            StringAssert.Contains(resultado.Recuperadas.Single(), "28/09");
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

        private static IPrincipal Con(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        // Modo ensayo (como el de las salidas): el mismo código, en una transacción que se deshace siempre. Solo Admin o Dirección
        [TestMethod]
        public async Task Ensayo_SinSerAdminNiDireccion_NoSeEnsaya()
        {
            A.CallTo(() => compras.PuedeTerminar(usuario, A<string>.Ignored, A<string>.Ignored)).Returns(true);

            UnauthorizedAccessException ex = await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() =>
                servicio.Terminar("COMP", "1", "ALG", "65", Terminar(("A", 1)), usuario, ensayo: true));

            StringAssert.Contains(ex.Message, "ensayo");
            A.CallTo(() => compras.Terminar(A<SolicitudTerminarRecepcion>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Ensayo_AdminSinPermisoDelTipo_SeEnsayaYDevuelveLasFilasAntesYDespues()
        {
            IPrincipal carlos = Con("NUEVAVISION\\Carlos", "Admin");
            A.CallTo(() => reposiciones.SeTerminaDesdeAqui).Returns(true);
            A.CallTo(() => reposiciones.PuedeTerminar(carlos, A<string>.Ignored, A<string>.Ignored)).Returns(false);
            SolicitudTerminarRecepcion recibida = null;
            A.CallTo(() => reposiciones.Terminar(A<SolicitudTerminarRecepcion>.Ignored)).ReturnsLazily(async (SolicitudTerminarRecepcion s) =>
            {
                recibida = s;
                int vez = 0;
                await s.Ensayo.Empezar(() => Task.FromResult(new List<FilaEnsayoDTO> { new FilaEnsayoDTO { Tabla = "T", Clave = "1", Datos = "vez " + ++vez } }));
                await s.Ensayo.Acabar();
                return new ResultadoTerminarRecepcionDTO { Tipo = "REPO", Documento = "80871" };
            });

            ResultadoTerminarRecepcionDTO resultado = await servicio.Terminar("REPO", "1", "ALG", "80871", Terminar(("A", 1)), carlos, ensayo: true);

            Assert.IsNotNull(recibida.Ensayo, "La estrategia tiene que saber que es un ensayo (deshace siempre)");
            Assert.IsTrue(resultado.Ensayo);
            Assert.AreEqual("vez 1", resultado.FilasAntes.Single().Datos);
            Assert.AreEqual("vez 2", resultado.FilasDespues.Single().Datos);
            Assert.IsNull(resultado.ErrorEnsayo);
            StringAssert.Contains(resultado.Avisos.First(), "ENSAYO");
        }

        [TestMethod]
        public async Task Ensayo_SiFalla_DevuelveElErrorRealConLasFilasDeAntesYNoLanza()
        {
            IPrincipal carlos = Con("Carlos", "NUEVAVISION\\Dirección");
            A.CallTo(() => compras.Terminar(A<SolicitudTerminarRecepcion>.Ignored)).ReturnsLazily(async (SolicitudTerminarRecepcion s) =>
            {
                await s.Ensayo.Empezar(() => Task.FromResult(new List<FilaEnsayoDTO> { new FilaEnsayoDTO { Tabla = "LinPedidoCmp", Clave = "7" } }));
                if (s.Ensayo != null)
                {
                    throw new NestoBusinessException("No se pudo determinar la ubicacion", new Exception("detalle del procedimiento"));
                }
                return new ResultadoTerminarRecepcionDTO();
            });

            ResultadoTerminarRecepcionDTO resultado = await servicio.Terminar("COMP", "1", "ALG", "65", Terminar(("A", 1)), carlos, ensayo: true);

            Assert.IsTrue(resultado.Ensayo);
            StringAssert.Contains(resultado.ErrorEnsayo, "No se pudo determinar la ubicacion");
            StringAssert.Contains(resultado.ErrorEnsayo, "detalle del procedimiento");
            Assert.AreEqual("7", resultado.FilasAntes.Single().Clave);
            Assert.IsNull(resultado.FilasDespues);
        }

        [TestMethod]
        public async Task DeVerdad_NoEsUnEnsayo()
        {
            A.CallTo(() => compras.PuedeTerminar(usuario, A<string>.Ignored, A<string>.Ignored)).Returns(true);
            SolicitudTerminarRecepcion recibida = null;
            A.CallTo(() => compras.Terminar(A<SolicitudTerminarRecepcion>.Ignored))
                .Invokes((SolicitudTerminarRecepcion s) => recibida = s)
                .Returns(new ResultadoTerminarRecepcionDTO());

            ResultadoTerminarRecepcionDTO resultado = await servicio.Terminar("COMP", "1", "ALG", "65", Terminar(("A", 1)), usuario);

            Assert.IsNull(recibida.Ensayo);
            Assert.IsFalse(resultado.Ensayo);
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
