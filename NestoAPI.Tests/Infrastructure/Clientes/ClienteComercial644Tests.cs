using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>
    /// Nesto#644 (hallazgos 5, 6, 7 y 15): lo que la auditoría de la ficha comercial dejó para la API.
    /// </summary>
    [TestClass]
    public class ClienteComercial644Tests
    {
        private const string EMPRESA = "1";
        private const string CLIENTE = "40445";
        private static readonly DateTime HOY = new DateTime(2026, 11, 26, 18, 0, 0);

        // ---------- (7) permisos: país y estado de peluquería ----------

        private static DatosPermisoClienteComercial RasoJE()
        {
            return new DatosPermisoClienteComercial
            {
                VendedorDelUsuario = "JE",
                EquipoDelUsuario = new List<string> { "JE" },
                VendedorActual = "MPP"
            };
        }

        [TestMethod]
        public void Evaluar_SoloElPaisDeUnClienteDeFuera_NoSePuede()
        {
            DatosPermisoClienteComercial datos = RasoJE();
            datos.CambiaPais = true;

            ResultadoPermisoClienteComercial resultado = ValidadorCambioClienteComercial.Evaluar(datos);

            Assert.IsFalse(resultado.Permitido, "Antes el país pasaba sin mirar el equipo");
            StringAssert.Contains(resultado.Motivo, "MPP");
        }

        [TestMethod]
        public void Evaluar_SoloElEstadoDePeluqueriaDeUnClienteDeFuera_NoSePuede()
        {
            DatosPermisoClienteComercial datos = RasoJE();
            datos.CambiaEstadoGrupo = true;

            Assert.IsFalse(ValidadorCambioClienteComercial.Evaluar(datos).Permitido);
        }

        [TestMethod]
        public void Evaluar_CrearLaFilaDePeluqueriaParaAlguienDeFuera_NoSePuede()
        {
            DatosPermisoClienteComercial datos = RasoJE();
            datos.VendedorActual = "JE";
            datos.CreaFilaGrupo = true;
            datos.CambiaEstadoGrupo = true;
            datos.VendedorGrupoDestino = "MPP";

            Assert.IsFalse(ValidadorCambioClienteComercial.Evaluar(datos).Permitido);

            datos.VendedorGrupoDestino = "JE";
            Assert.IsTrue(ValidadorCambioClienteComercial.Evaluar(datos).Permitido, "para él mismo, sí");
        }

        [TestMethod]
        public void Evaluar_SinCambiosDePaisNiPeluqueria_SigueComoAntes()
        {
            Assert.IsTrue(ValidadorCambioClienteComercial.Evaluar(RasoJE()).Permitido);
        }

        [TestMethod]
        public void CambiaElPais_SinRellenoNiMayusculas()
        {
            Assert.IsFalse(ValidadorCambioClienteComercial.CambiaElPais("ES", "es "));
            Assert.IsFalse(ValidadorCambioClienteComercial.CambiaElPais("ES", null));
            Assert.IsFalse(ValidadorCambioClienteComercial.CambiaElPais("ES", "  "));
            Assert.IsTrue(ValidadorCambioClienteComercial.CambiaElPais("ES", "PT"));
            Assert.IsTrue(ValidadorCambioClienteComercial.CambiaElPais(null, "PT"));
        }

        [TestMethod]
        public void CambiaElEstadoDelGrupo_DistintoONoExisteLaFila()
        {
            var fila = new VendedorClienteGrupoProducto { Vendedor = "JE", Estado = 0 };
            Assert.IsFalse(ValidadorCambioClienteComercial.CambiaElEstadoDelGrupo(fila, null));
            Assert.IsFalse(ValidadorCambioClienteComercial.CambiaElEstadoDelGrupo(fila, new VendedorGrupoProductoDTO { estado = 0 }));
            Assert.IsFalse(ValidadorCambioClienteComercial.CambiaElEstadoDelGrupo(fila, new VendedorGrupoProductoDTO { estado = null }));
            Assert.IsTrue(ValidadorCambioClienteComercial.CambiaElEstadoDelGrupo(fila, new VendedorGrupoProductoDTO { estado = 9 }));
            Assert.IsTrue(ValidadorCambioClienteComercial.CambiaElEstadoDelGrupo(null, new VendedorGrupoProductoDTO { vendedor = "JE" }));
        }

        // ---------- (6) vendedor por grupo: null = no tocar; al crear, el estado que viene ----------

        [TestMethod]
        public void AplicarVendedorGrupo_VendedorNull_NoLoToca()
        {
            var actual = new VendedorClienteGrupoProducto { Vendedor = "IF ", Usuario = "Manuel", Estado = 0 };

            GestorComisiones.AplicarVendedorGrupo(actual, new VendedorGrupoProductoDTO { vendedor = null, estado = 9 }, "Sancho");

            Assert.AreEqual("IF ", actual.Vendedor, "antes lo dejaba a null");
            Assert.AreEqual((short)9, actual.Estado);
            Assert.AreEqual("Manuel", actual.Usuario, "sin cambio de vendedor, la auditoría se queda");
        }

        [TestMethod]
        public void AplicarVendedorGrupo_EstadoNull_NoLoToca()
        {
            var actual = new VendedorClienteGrupoProducto { Vendedor = "IF ", Usuario = "Manuel", Estado = 7 };

            GestorComisiones.AplicarVendedorGrupo(actual, new VendedorGrupoProductoDTO { vendedor = "NV", estado = null }, "Sancho");

            Assert.AreEqual("NV", actual.Vendedor);
            Assert.AreEqual((short)7, actual.Estado, "antes lo ponía a 0");
        }

        [TestMethod]
        public void ActualizarVendedorClienteGrupoProducto_AlCrearLaFila_UsaElEstadoQueViene()
        {
            NVEntities db = Db(new Cliente { Empresa = EMPRESA, Nº_Cliente = CLIENTE, Contacto = "0", Estado = 0, Vendedor = "NV" }, null);
            VendedorClienteGrupoProducto creada = null;
            A.CallTo(() => db.VendedoresClientesGruposProductos.Add(A<VendedorClienteGrupoProducto>._))
                .Invokes((VendedorClienteGrupoProducto v) => creada = v);
            var dto = new ClienteDTO { VendedoresGrupoProducto = new List<VendedorGrupoProductoDTO> { new VendedorGrupoProductoDTO { grupoProducto = "PEL", vendedor = "JE", estado = 9 } } };

            GestorComisiones.ActualizarVendedorClienteGrupoProducto(db, db.Clientes.First(), dto, "Sancho");

            Assert.IsNotNull(creada);
            Assert.AreEqual((short)9, creada.Estado, "antes, siempre VISITA_PRESENCIAL");
            Assert.AreEqual("JE", creada.Vendedor);
        }

        [TestMethod]
        public void ActualizarVendedorClienteGrupoProducto_AlCrearSinEstado_VisitaPresencial()
        {
            NVEntities db = Db(new Cliente { Empresa = EMPRESA, Nº_Cliente = CLIENTE, Contacto = "0", Estado = 0, Vendedor = "NV" }, null);
            VendedorClienteGrupoProducto creada = null;
            A.CallTo(() => db.VendedoresClientesGruposProductos.Add(A<VendedorClienteGrupoProducto>._))
                .Invokes((VendedorClienteGrupoProducto v) => creada = v);
            var dto = new ClienteDTO { VendedoresGrupoProducto = new List<VendedorGrupoProductoDTO> { new VendedorGrupoProductoDTO { grupoProducto = "PEL", vendedor = "JE" } } };

            GestorComisiones.ActualizarVendedorClienteGrupoProducto(db, db.Clientes.First(), dto, "Sancho");

            Assert.AreEqual(Constantes.Clientes.Estados.VISITA_PRESENCIAL, creada.Estado);
        }

        [TestMethod]
        public void VendedorGrupoProductoDTO_SinEstado_NoViajaEnElJson()
        {
            // Los vendedores del pedido no tienen estado: quien lo lee como short no-anulable no puede recibir un null.
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(new VendedorGrupoProductoDTO { vendedor = "JE", grupoProducto = "PEL" });
            Assert.IsFalse(json.Contains("estado"), json);
            string conEstado = Newtonsoft.Json.JsonConvert.SerializeObject(new VendedorGrupoProductoDTO { vendedor = "JE", estado = 0 });
            StringAssert.Contains(conEstado, "\"estado\":0");
        }

        // ---------- (15) auditoría del token ----------

        [TestMethod]
        public void AplicarCambiosClienteComercial_ConUsuarioDeAuditoria_ManaSobreElDelDto()
        {
            var clienteDB = new Cliente { Estado = 5, Vendedor = "NV ", Usuario = "viejo" };
            var dto = new ClienteDTO { estado = 9, usuario = "loquediga" };

            ClientesController.AplicarCambiosClienteComercial(clienteDB, dto, @"NUEVAVISION\Carlos");

            Assert.AreEqual(@"NUEVAVISION\Carlos", clienteDB.Usuario);
        }

        [TestMethod]
        public void AplicarCambiosClienteComercial_SinNingunUsuario_NuncaVacio()
        {
            var clienteDB = new Cliente { Estado = 5, Vendedor = "NV ", Usuario = "viejo" };

            ClientesController.AplicarCambiosClienteComercial(clienteDB, new ClienteDTO { estado = 9 }, null);

            Assert.AreEqual(UsuarioAuditoriaHelper.DESCONOCIDO, clienteDB.Usuario);
        }

        // ---------- (5) copia vieja ----------

        private static GuardadoClienteComercial Guardado()
        {
            return new GuardadoClienteComercial
            {
                Estado = 0,
                Vendedor = "JE ",
                Pais = "ES",
                Usuario = @"NUEVAVISION\Aida",
                Fecha = new DateTime(2026, 11, 26, 15, 14, 0),
                FilaGrupoExiste = true,
                VendedorPeluqueria = "IF ",
                EstadoPeluqueria = 0,
                UsuarioGrupo = @"NUEVAVISION\Manuel",
                FechaGrupo = new DateTime(2026, 11, 26, 9, 0, 0)
            };
        }

        private static OriginalClienteComercialDTO OriginalIgual()
        {
            return new OriginalClienteComercialDTO { estado = 0, vendedor = "JE", pais = "es", vendedorPeluqueria = "IF", estadoPeluqueria = 0 };
        }

        [TestMethod]
        public void Regla_SinOriginal_NuncaHayConflicto()
        {
            var cambios = new ClienteDTO { cliente = CLIENTE, estado = 9 };
            Assert.IsNull(ReglaClienteComercialCambiado.Evaluar(Guardado(), cambios, HOY));
        }

        [TestMethod]
        public void Regla_OriginalIgualAloGuardado_SinConflicto()
        {
            var cambios = new ClienteDTO { cliente = CLIENTE, estado = 9, vendedor = "DV", pais = "PT", original = OriginalIgual() };
            Assert.IsNull(ReglaClienteComercialCambiado.Evaluar(Guardado(), cambios, HOY));
        }

        [TestMethod]
        public void Regla_OtroCambioElEstadoYYoTambien_Conflicto()
        {
            OriginalClienteComercialDTO original = OriginalIgual();
            original.estado = 5;
            var cambios = new ClienteDTO { cliente = CLIENTE, estado = 9, original = original };

            ConflictoClienteComercial conflicto = ReglaClienteComercialCambiado.Evaluar(Guardado(), cambios, HOY);

            Assert.IsNotNull(conflicto);
            CollectionAssert.AreEqual(new[] { "estado" }, conflicto.Campos);
            Assert.AreEqual(@"NUEVAVISION\Aida", conflicto.Usuario);
            Assert.AreEqual("La ficha del cliente 40445 la ha cambiado Aida a las 15:14 desde que usted la abrió (estado). " +
                "Recárguela y vuelva a hacer los cambios.", conflicto.Mensaje);
        }

        [TestMethod]
        public void Regla_OtroCambioLoQueYoNoToco_NoHayConflicto()
        {
            // Otro cambió el estado; este PUT solo cambia el vendedor: no se pisa el estado, no hay por qué parar.
            OriginalClienteComercialDTO original = OriginalIgual();
            original.estado = 5;
            var cambios = new ClienteDTO { cliente = CLIENTE, estado = null, vendedor = "DV", original = original };

            Assert.IsNull(ReglaClienteComercialCambiado.Evaluar(Guardado(), cambios, HOY));
        }

        [TestMethod]
        public void Regla_VariosCampos_YPeluqueria_LaFechaMasReciente()
        {
            OriginalClienteComercialDTO original = new OriginalClienteComercialDTO { estado = 5, vendedor = "MPP", pais = "ES", vendedorPeluqueria = "JE", estadoPeluqueria = 3 };
            GuardadoClienteComercial guardado = Guardado();
            guardado.FechaGrupo = new DateTime(2026, 11, 25, 16, 30, 0);
            guardado.Fecha = new DateTime(2026, 11, 24, 10, 0, 0);
            var cambios = new ClienteDTO
            {
                cliente = CLIENTE,
                estado = 9,
                vendedor = "DV",
                VendedoresGrupoProducto = new List<VendedorGrupoProductoDTO> { new VendedorGrupoProductoDTO { vendedor = "NV", estado = 9 } },
                original = original
            };

            ConflictoClienteComercial conflicto = ReglaClienteComercialCambiado.Evaluar(guardado, cambios, HOY);

            CollectionAssert.AreEqual(new[] { "estado", "vendedor", "vendedorPeluqueria", "estadoPeluqueria" }, conflicto.Campos);
            Assert.AreEqual(@"NUEVAVISION\Manuel", conflicto.Usuario, "la fila de peluquería es lo más reciente");
            StringAssert.Contains(conflicto.Mensaje, "Manuel el 25/11 a las 16:30");
            StringAssert.Contains(conflicto.Mensaje, "(estado, vendedor, vendedor de peluquería, estado de peluquería)");
        }

        [TestMethod]
        public void Regla_PeluqueriaSinFila_YOriginalSinVendedor_SinConflicto()
        {
            // Nesto manda estadoPeluqueria = 0 cuando el cliente no tenía fila
            GuardadoClienteComercial guardado = Guardado();
            guardado.FilaGrupoExiste = false;
            guardado.VendedorPeluqueria = null;
            guardado.EstadoPeluqueria = null;
            var original = new OriginalClienteComercialDTO { estado = 0, vendedor = "JE", pais = "ES", vendedorPeluqueria = null, estadoPeluqueria = 0 };
            var cambios = new ClienteDTO
            {
                cliente = CLIENTE,
                VendedoresGrupoProducto = new List<VendedorGrupoProductoDTO> { new VendedorGrupoProductoDTO { vendedor = "JE", estado = 9 } },
                original = original
            };

            Assert.IsNull(ReglaClienteComercialCambiado.Evaluar(guardado, cambios, HOY));
        }

        [TestMethod]
        public void Regla_OtroCreoLaFilaDePeluqueria_Conflicto()
        {
            var original = new OriginalClienteComercialDTO { estado = 0, vendedor = "JE", pais = "ES", vendedorPeluqueria = null, estadoPeluqueria = 0 };
            var cambios = new ClienteDTO
            {
                cliente = CLIENTE,
                VendedoresGrupoProducto = new List<VendedorGrupoProductoDTO> { new VendedorGrupoProductoDTO { vendedor = "JE", estado = 0 } },
                original = original
            };

            ConflictoClienteComercial conflicto = ReglaClienteComercialCambiado.Evaluar(Guardado(), cambios, HOY);

            CollectionAssert.AreEqual(new[] { "vendedorPeluqueria" }, conflicto.Campos);
        }

        [TestMethod]
        public void Regla_PaisCambiadoPorOtro_Conflicto()
        {
            OriginalClienteComercialDTO original = OriginalIgual();
            original.pais = "FR";
            var cambios = new ClienteDTO { cliente = CLIENTE, pais = "PT", original = original };

            CollectionAssert.AreEqual(new[] { "pais" }, ReglaClienteComercialCambiado.Evaluar(Guardado(), cambios, HOY).Campos);
            StringAssert.Contains(ReglaClienteComercialCambiado.Evaluar(Guardado(), cambios, HOY).Mensaje, "(país)");
        }

        // ---------- el PUT entero ----------

        [TestMethod]
        public async Task PutCliente_CopiaVieja_409SinGuardar()
        {
            NVEntities db = Db(new Cliente { Empresa = EMPRESA, Nº_Cliente = CLIENTE, Contacto = "0", Estado = 5, Vendedor = "NV", Usuario = "Aida", Fecha_Modificación = HOY }, null);
            var controller = Controlador(db);
            var dto = new ClienteDTO
            {
                empresa = EMPRESA, cliente = CLIENTE, contacto = "0", estado = 9,
                original = new OriginalClienteComercialDTO { estado = 0, vendedor = "NV" }
            };

            var resultado = await controller.PutCliente(dto);

            Assert.IsInstanceOfType(resultado, typeof(NegotiatedContentResult<object>));
            var negociado = (NegotiatedContentResult<object>)resultado;
            Assert.AreEqual(HttpStatusCode.Conflict, negociado.StatusCode);
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(negociado.Content);
            StringAssert.Contains(json, "\"Codigo\":\"CLIENTE_CAMBIADO\"");
            StringAssert.Contains(json, "\"campos\":[\"estado\"]");
            Assert.AreEqual((short)5, db.Clientes.First().Estado, "no se aplica nada");
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PutCliente_OriginalAlDia_GuardaConElUsuarioDelToken()
        {
            var clienteDB = new Cliente { Empresa = EMPRESA, Nº_Cliente = CLIENTE, Contacto = "0", Estado = 0, Vendedor = "NV", Usuario = "viejo" };
            NVEntities db = Db(clienteDB, null);
            var controller = Controlador(db);
            controller.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, @"NUEVAVISION\Carlos"),
                new Claim(ClaimTypes.Role, @"NUEVAVISION\Dirección"),
                new Claim("Vendedor", "CA")
            }, "Bearer"));
            var dto = new ClienteDTO
            {
                empresa = EMPRESA, cliente = CLIENTE, contacto = "0", vendedor = "DV", usuario = "otro",
                original = new OriginalClienteComercialDTO { estado = 0, vendedor = "NV" }
            };

            var resultado = await controller.PutCliente(dto);

            Assert.IsInstanceOfType(resultado, typeof(StatusCodeResult));
            Assert.AreEqual("DV", clienteDB.Vendedor);
            Assert.AreEqual(@"NUEVAVISION\Carlos", clienteDB.Usuario, "Nesto#644 (15): el del token, no el del DTO");
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
        }

        private static ClientesController Controlador(NVEntities db)
        {
            return new ClientesController(A.Fake<IGestorClientes>(), A.Fake<IServicioVendedores>(), A.Fake<IGestorSincronizacion>(), null, db);
        }

        private static NVEntities Db(Cliente cliente, VendedorClienteGrupoProducto fila)
        {
            NVEntities db = A.Fake<NVEntities>();
            DbSet<Cliente> clientes = A.Fake<DbSet<Cliente>>(o => o.Implements<IQueryable<Cliente>>().Implements<IDbAsyncEnumerable<Cliente>>());
            DbSet<VendedorClienteGrupoProducto> grupos = A.Fake<DbSet<VendedorClienteGrupoProducto>>(o => o.Implements<IQueryable<VendedorClienteGrupoProducto>>().Implements<IDbAsyncEnumerable<VendedorClienteGrupoProducto>>());
            A.CallTo(() => db.Clientes).Returns(clientes);
            A.CallTo(() => db.VendedoresClientesGruposProductos).Returns(grupos);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));
            Configurar(clientes, new List<Cliente> { cliente }.AsQueryable());
            Configurar(grupos, (fila == null ? new List<VendedorClienteGrupoProducto>() : new List<VendedorClienteGrupoProducto> { fila }).AsQueryable());
            return db;
        }

        private static void Configurar<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
        }
    }
}
