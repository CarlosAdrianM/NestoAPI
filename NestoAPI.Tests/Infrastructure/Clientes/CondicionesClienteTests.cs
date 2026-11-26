using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Vendedores;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>
    /// Carlos 26/11/26: el IVA de la ficha (caso 42178) y los descuentos pactados (caso 35253, Lisap Keraplant al 50 %), que hasta hoy
    /// solo se podían hacer con scripts de sa.
    /// </summary>
    [TestClass]
    public class CondicionesClienteTests
    {
        private IRepositorioCondicionesCliente repo;
        private List<string> publicados;

        private static ClaimsPrincipal Usuario(params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, "NUEVAVISION\\Carlos") };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private static readonly ClaimsPrincipal DIRECCION = Usuario("Dirección");
        private static readonly ClaimsPrincipal ADMINISTRACION = Usuario("Administración");
        private static readonly ClaimsPrincipal VENDEDOR = Usuario("Vendedores");

        [TestInitialize]
        public void Preparar()
        {
            publicados = new List<string>();
            repo = A.Fake<IRepositorioCondicionesCliente>();
            A.CallTo(() => repo.IvasValidos("1")).Returns(new List<string> { "G21", "E52", "R52", "EXE" });
            A.CallTo(() => repo.LeerFichas("1", "42178")).Returns(new List<FichaIvaCliente>
            {
                new FichaIvaCliente { Contacto = "0", Iva = "G21" },
                new FichaIvaCliente { Contacto = "1", Iva = "G21" }
            });
            A.CallTo(() => repo.LeerFichas("1", "35253")).Returns(new List<FichaIvaCliente> { new FichaIvaCliente { Contacto = "0", Iva = "G21" } });
            A.CallTo(() => repo.LeerDescuentos("1", "35253")).Returns(new List<DescuentoPactadoDTO>());
            A.CallTo(() => repo.ProductosAfectados("1", A<DescuentoPactadoPeticionDTO>._)).Returns((125, "41001", "KERAPLANT CHAMPÚ", (decimal?)20m));
            A.CallTo(() => repo.CrearDescuento("1", "35253", A<DescuentoPactadoPeticionDTO>._, A<string>._, A<string>._)).Returns((int?)987654);
        }

        private ServicioCondicionesCliente Servicio() => new ServicioCondicionesCliente(repo);

        private Task Publicar(string empresa, string cliente)
        {
            publicados.Add(empresa + "/" + cliente);
            return Task.CompletedTask;
        }

        private static DescuentoPactadoPeticionDTO Keraplant() => new DescuentoPactadoPeticionDTO { Familia = " Lisap ", FiltroProducto = "KERAPLANT", Descuento = 0.50m };

        // ---------- IVA ----------

        [TestMethod]
        public async Task Iva_Administracion_CambiaTodosLosContactosConRastroYPublica()
        {
            var r = await Servicio().CambiarIva("1", "42178", new CambioIvaClienteDTO { Iva = "e52" }, ADMINISTRACION, Publicar);

            Assert.AreEqual(200, r.Estado, r.Motivo);
            Assert.IsTrue(r.Datos.Cambiado);
            Assert.AreEqual("E52", r.Datos.Iva, "como está en ParametrosIVA");
            CollectionAssert.AreEqual(new[] { "Contacto 0: G21 → E52", "Contacto 1: G21 → E52" }, r.Datos.Cambios);
            A.CallTo(() => repo.CambiarIva("1", "42178", "E52", "NUEVAVISION\\Carlos", "1|42178: 0=G21, 1=G21", "1|42178: E52")).MustHaveHappenedOnceExactly();
            CollectionAssert.AreEqual(new[] { "1/42178" }, publicados);
        }

        [TestMethod]
        public async Task Iva_QueNoExisteEnLosParametros_400SinTocarNada()
        {
            var r = await Servicio().CambiarIva("1", "42178", new CambioIvaClienteDTO { Iva = "X99" }, DIRECCION, Publicar);

            Assert.AreEqual(400, r.Estado);
            StringAssert.Contains(r.Motivo, "X99");
            StringAssert.Contains(r.Motivo, "E52");
            A.CallTo(() => repo.CambiarIva(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Iva_Vacio_400()
        {
            var r = await Servicio().CambiarIva("1", "42178", new CambioIvaClienteDTO { Iva = " " }, DIRECCION, Publicar);

            Assert.AreEqual(400, r.Estado);
        }

        [TestMethod]
        public async Task Iva_UnVendedor_403()
        {
            var r = await Servicio().CambiarIva("1", "42178", new CambioIvaClienteDTO { Iva = "E52" }, VENDEDOR, Publicar);

            Assert.AreEqual(403, r.Estado);
            A.CallTo(() => repo.IvasValidos(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Iva_ClienteDeLaTienda_403AunqueTengaGrupo()
        {
            var tienda = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("cliente", "42178"), new Claim(ClaimTypes.Role, "Dirección") }, "prueba"));

            Assert.AreEqual(403, (await Servicio().CambiarIva("1", "42178", new CambioIvaClienteDTO { Iva = "E52" }, tienda, Publicar)).Estado);
        }

        [TestMethod]
        public async Task Iva_YaLoTenia_200SinEscribirNiPublicar()
        {
            var r = await Servicio().CambiarIva("1", "42178", new CambioIvaClienteDTO { Iva = "G21" }, DIRECCION, Publicar);

            Assert.AreEqual(200, r.Estado);
            Assert.IsFalse(r.Datos.Cambiado);
            A.CallTo(() => repo.CambiarIva(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
            Assert.AreEqual(0, publicados.Count);
        }

        [TestMethod]
        public async Task Iva_ClienteQueNoExiste_404()
        {
            A.CallTo(() => repo.LeerFichas("1", "99999")).Returns(new List<FichaIvaCliente>());

            Assert.AreEqual(404, (await Servicio().CambiarIva("1", "99999", new CambioIvaClienteDTO { Iva = "E52" }, DIRECCION, Publicar)).Estado);
        }

        [TestMethod]
        public async Task Iva_SiOdooFalla_QuedaCambiadoYLoDice()
        {
            var r = await Servicio().CambiarIva("1", "42178", new CambioIvaClienteDTO { Iva = "E52" }, DIRECCION, (e, c) => throw new InvalidOperationException("Odoo caído"));

            Assert.AreEqual(200, r.Estado);
            Assert.IsTrue(r.Datos.Cambiado);
            Assert.AreEqual(1, r.Datos.Avisos.Count);
        }

        // ---------- Descuentos pactados ----------

        [TestMethod]
        public async Task Descuento_Ensayo_DiceACuantosYElEjemploSinEscribir()
        {
            var r = await Servicio().Crear("1", "35253", Keraplant(), true, DIRECCION);

            Assert.AreEqual(200, r.Estado, r.Motivo);
            Assert.IsTrue(r.Datos.Ensayo);
            Assert.AreEqual(125, r.Datos.ProductosAfectados);
            Assert.AreEqual(10m, r.Datos.Ejemplo.PrecioConDescuento);
            Assert.AreEqual(0.5m, r.Datos.Ejemplo.DescuentoEfectivo);
            StringAssert.Contains(r.Datos.Mensaje, "nombre que empieza por «KERAPLANT»");
            A.CallTo(() => repo.CrearDescuento(A<string>._, A<string>._, A<DescuentoPactadoPeticionDTO>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repo.ProductosAfectados("1", A<DescuentoPactadoPeticionDTO>.That.Matches(p => p.Familia == "Lisap" && p.FiltroProducto == "KERAPLANT" && p.CantidadMinima == 1)))
                .MustHaveHappened();
        }

        [TestMethod]
        public async Task Descuento_DeVerdad_CreaUnaFilaConRastro()
        {
            A.CallTo(() => repo.LeerDescuentos("1", "35253")).ReturnsNextFromSequence(
                new List<DescuentoPactadoDTO>(),
                new List<DescuentoPactadoDTO> { new DescuentoPactadoDTO { NumeroOrden = 987654, Familia = "Lisap", FiltroProducto = "KERAPLANT", CantidadMinima = 1, Descuento = 0.5m } });

            var r = await Servicio().Crear("1", "35253", Keraplant(), false, DIRECCION);

            Assert.AreEqual(200, r.Estado, r.Motivo);
            Assert.IsTrue(r.Datos.Creada);
            Assert.AreEqual(987654, r.Datos.Fila.NumeroOrden);
            A.CallTo(() => repo.CrearDescuento("1", "35253", A<DescuentoPactadoPeticionDTO>._, "NUEVAVISION\\Carlos", A<string>.That.Contains("KERAPLANT")))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Descuento_YaHayUnaIgualConElMismoValor_NoCreaOtra()
        {
            A.CallTo(() => repo.LeerDescuentos("1", "35253")).Returns(new List<DescuentoPactadoDTO>
            {
                new DescuentoPactadoDTO { NumeroOrden = 5, Familia = "LISAP", FiltroProducto = "KERAPLANT ", CantidadMinima = 1, Descuento = 0.5m }
            });

            var r = await Servicio().Crear("1", "35253", Keraplant(), false, DIRECCION);

            Assert.AreEqual(200, r.Estado);
            Assert.IsFalse(r.Datos.Creada);
            Assert.AreEqual(5, r.Datos.Fila.NumeroOrden);
            A.CallTo(() => repo.CrearDescuento(A<string>._, A<string>._, A<DescuentoPactadoPeticionDTO>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Descuento_YaHayUnaIgualConOtroValor_409()
        {
            A.CallTo(() => repo.LeerDescuentos("1", "35253")).Returns(new List<DescuentoPactadoDTO>
            {
                new DescuentoPactadoDTO { NumeroOrden = 5, Familia = "Lisap", FiltroProducto = "KERAPLANT", CantidadMinima = 1, Descuento = 0.4m }
            });

            var r = await Servicio().Crear("1", "35253", Keraplant(), false, DIRECCION);

            Assert.AreEqual(409, r.Estado);
            Assert.AreEqual(ReglasCondicionesCliente.CODIGO_YA_EXISTE, r.Codigo);
        }

        [TestMethod]
        public async Task Descuento_UnaDeContactoODeCampana_NoEsLaMismaClave()
        {
            A.CallTo(() => repo.LeerDescuentos("1", "35253")).ReturnsNextFromSequence(
                new List<DescuentoPactadoDTO>
                {
                    new DescuentoPactadoDTO { NumeroOrden = 5, Contacto = "1", Familia = "Lisap", FiltroProducto = "KERAPLANT", CantidadMinima = 1, Descuento = 0.4m },
                    new DescuentoPactadoDTO { NumeroOrden = 6, Campana = "BF", Familia = "Lisap", FiltroProducto = "KERAPLANT", CantidadMinima = 1, Descuento = 0.4m }
                },
                new List<DescuentoPactadoDTO>());

            var r = await Servicio().Crear("1", "35253", Keraplant(), false, DIRECCION);

            Assert.AreEqual(200, r.Estado, r.Motivo);
            Assert.IsTrue(r.Datos.Creada);
        }

        [TestMethod]
        public async Task Descuento_SinProductosVivos_EnsayoAvisaYDeVerdad400()
        {
            A.CallTo(() => repo.ProductosAfectados("1", A<DescuentoPactadoPeticionDTO>._)).Returns((0, (string)null, (string)null, (decimal?)null));

            var ensayo = await Servicio().Crear("1", "35253", Keraplant(), true, DIRECCION);
            var deVerdad = await Servicio().Crear("1", "35253", Keraplant(), false, DIRECCION);

            Assert.AreEqual(200, ensayo.Estado);
            Assert.AreEqual(1, ensayo.Datos.Avisos.Count);
            Assert.AreEqual(400, deVerdad.Estado);
        }

        [TestMethod]
        public async Task Descuento_Administracion_403()
        {
            Assert.AreEqual(403, (await Servicio().Crear("1", "35253", Keraplant(), true, ADMINISTRACION)).Estado);
            Assert.AreEqual(403, (await Servicio().Borrar("1", "35253", 5, ADMINISTRACION)).Estado);
            Assert.AreEqual(200, (await Servicio().Leer("1", "35253", ADMINISTRACION)).Estado, "leerlos sí");
            Assert.AreEqual(403, (await Servicio().Leer("1", "35253", VENDEDOR)).Estado);
        }

        [TestMethod]
        public void Descuento_Validaciones()
        {
            Func<DescuentoPactadoPeticionDTO, string> motivo = p => ReglasCondicionesCliente.MotivoNoValido(ReglasCondicionesCliente.Normalizar(p));

            Assert.IsNull(motivo(Keraplant()));
            Assert.IsNull(motivo(new DescuentoPactadoPeticionDTO { Producto = "41001", Precio = 9.5m }));
            Assert.IsNull(motivo(new DescuentoPactadoPeticionDTO { GrupoProducto = "PEL", Descuento = 0.1m }));
            Assert.IsNull(motivo(new DescuentoPactadoPeticionDTO { Familia = "Lisap", GrupoProducto = "PEL", Descuento = 0.1m, CantidadMinima = 6 }));
            Assert.IsNotNull(motivo(new DescuentoPactadoPeticionDTO { Descuento = 0.5m }), "sin a qué");
            Assert.IsNotNull(motivo(new DescuentoPactadoPeticionDTO { Familia = "Lisap", Descuento = 50m }), "50 no es tanto por uno");
            Assert.IsNotNull(motivo(new DescuentoPactadoPeticionDTO { Familia = "Lisap", Descuento = 1m }));
            Assert.IsNotNull(motivo(new DescuentoPactadoPeticionDTO { Familia = "Lisap", Descuento = 0m }));
            Assert.IsNotNull(motivo(new DescuentoPactadoPeticionDTO { Familia = "Lisap" }), "ni descuento ni precio");
            Assert.IsNotNull(motivo(new DescuentoPactadoPeticionDTO { Familia = "Lisap", Descuento = 0.5m, Precio = 3m }), "los dos");
            Assert.IsNotNull(motivo(new DescuentoPactadoPeticionDTO { Familia = "Lisap", Precio = 3m }), "precio sin producto");
            Assert.IsNotNull(motivo(new DescuentoPactadoPeticionDTO { FiltroProducto = "KERA", Descuento = 0.5m }), "filtro sin familia");
            Assert.IsNotNull(motivo(new DescuentoPactadoPeticionDTO { Producto = "41001", Familia = "Lisap", Descuento = 0.5m }), "producto con familia");
            Assert.IsNotNull(motivo(new DescuentoPactadoPeticionDTO { Familia = "Lisap", Descuento = 0.5m, CantidadMinima = 0 }));
        }

        [TestMethod]
        public async Task Borrar_SoloUnaFilaDeEseCliente()
        {
            A.CallTo(() => repo.LeerDescuentos("1", "35253")).Returns(new List<DescuentoPactadoDTO> { new DescuentoPactadoDTO { NumeroOrden = 5, Familia = "Lisap" } });
            A.CallTo(() => repo.BorrarDescuento("1", "35253", 5, A<string>._, A<string>._)).Returns(true);

            Assert.AreEqual(200, (await Servicio().Borrar("1", "35253", 5, DIRECCION)).Estado);
            Assert.AreEqual(404, (await Servicio().Borrar("1", "35253", 6, DIRECCION)).Estado, "la 6 no es de este cliente");
            A.CallTo(() => repo.BorrarDescuento("1", "35253", 6, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void LasConsultas_SinFuncionesQueLaBdNoTiene()
        {
            foreach (string sql in new[] { RepositorioCondicionesClienteSql.SQL_PRODUCTOS, RepositorioCondicionesClienteSql.SQL_CREAR, RepositorioCondicionesClienteSql.SQL_BORRAR,
                RepositorioCondicionesClienteSql.SQL_CAMBIAR_IVA })
            {
                Assert.IsFalse(sql.Contains("TRY_CONVERT") || sql.Contains("STRING_SPLIT") || sql.Contains("OFFSET") || sql.Contains("CONCAT("), sql);
            }
            StringAssert.Contains(RepositorioCondicionesClienteSql.SQL_PRODUCTOS, "LIKE @filtro + '%'");
            StringAssert.Contains(RepositorioCondicionesClienteSql.SQL_BORRAR, "[Nº Cliente] = @cliente");
        }

        // ---------- Controlador ----------

        [TestMethod]
        public async Task Controlador_PutIva_DevuelveLoQueDiceElServicio()
        {
            var controlador = new ClientesController(A.Fake<IGestorClientes>(), A.Fake<IServicioVendedores>())
            {
                RepositorioCondicionesCliente = repo,
                PublicarCliente = Publicar,
                User = VENDEDOR
            };

            IHttpActionResult r = await controlador.PutIvaCliente("1", "42178", new CambioIvaClienteDTO { Iva = "E52" });

            Assert.AreEqual(HttpStatusCode.Forbidden, (HttpStatusCode)r.GetType().GetProperty("StatusCode").GetValue(r));
        }
    }
}
