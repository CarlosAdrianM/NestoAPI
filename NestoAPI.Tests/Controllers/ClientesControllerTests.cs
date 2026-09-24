using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class ClientesControllerTests
    {
        // Regresión NestoAPI#201: GetClientes(empresa, filtro) lanzaba NullReferenceException
        // cuando filtro llegaba null (NestoApp manda ?filtro= al limpiar la búsqueda). Ahora debe
        // lanzar la excepción amistosa de "filtro de al menos 4 caracteres", no una NRE.
        [TestMethod]
        public void ClientesController_GetClientes_FiltroNull_NoLanzaNullReference()
        {
            ClientesController controller = new ClientesController(
                A.Fake<IGestorClientes>(),
                A.Fake<IServicioVendedores>(),
                A.Fake<IGestorSincronizacion>());

            try
            {
                _ = controller.GetClientes("1", null);
                Assert.Fail("Debía lanzar una excepción por filtro inválido");
            }
            catch (NullReferenceException)
            {
                Assert.Fail("No debe lanzar NullReferenceException");
            }
            catch (Exception ex)
            {
                Assert.AreEqual("Por favor, utilice un filtro de al menos 4 caracteres", ex.Message);
            }
        }

        /// <summary>
        /// NestoAPI#440: el filtro corto es USO NORMAL (alguien teclea dos letras y busca), no un
        /// fallo del sistema. Como Exception genérica acababa en ELMAH (7 fichas solo el 01/09) y
        /// salía como 500; como NestoBusinessException el filtro global responde 400 y NO la
        /// registra (#361), que es el circuito ya pensado para las denegaciones de negocio.
        /// </summary>
        [TestMethod]
        public void ClientesController_GetClientes_FiltroCorto_EsDenegacionDeNegocioSinElmah()
        {
            ClientesController controller = new ClientesController(
                A.Fake<IGestorClientes>(),
                A.Fake<IServicioVendedores>(),
                A.Fake<IGestorSincronizacion>());

            try
            {
                _ = controller.GetClientes("1", "ab");
                Assert.Fail("Debía lanzar por filtro demasiado corto");
            }
            catch (NestoAPI.Infraestructure.Exceptions.NestoBusinessException ex)
            {
                Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, ex.StatusCode);
                Assert.IsFalse(NestoAPI.Infraestructure.Filters.GlobalExceptionFilter.DebeRegistrarseEnElmah(ex),
                    "Una denegación de negocio no debe generar ficha en ELMAH");
            }
        }

        // NestoAPI#393 (Vendedores → Clientes): cambiar SOLO el estado se descartaba en
        // silencio porque su asignación vivía dentro del if del cambio de vendedor (y el PUT
        // devolvía 204 + "guardado correctamente" en Nesto sin cambiar nada).

        [TestMethod]
        public void AplicarCambiosClienteComercial_SoloCambiaElEstado_SePersisteSinTocarVendedor()
        {
            var clienteDB = new Cliente { Estado = 5, Vendedor = "NV ", Usuario = "viejo" };
            var dto = new ClienteDTO { estado = 9, vendedor = "NV", usuario = "carlos" };

            ClientesController.AplicarCambiosClienteComercial(clienteDB, dto);

            Assert.AreEqual((short)9, clienteDB.Estado);
            Assert.AreEqual("carlos", clienteDB.Usuario, "El cambio de estado audita su usuario");
            Assert.AreEqual("NV ", clienteDB.Vendedor, "El vendedor no cambió y no se toca");
        }

        [TestMethod]
        public void AplicarCambiosClienteComercial_CambianVendedorYEstado_SePersistenAmbos()
        {
            var clienteDB = new Cliente { Estado = 5, Vendedor = "NV ", Usuario = "viejo" };
            var dto = new ClienteDTO { estado = 9, vendedor = "DV", usuario = "carlos" };

            ClientesController.AplicarCambiosClienteComercial(clienteDB, dto);

            Assert.AreEqual((short)9, clienteDB.Estado);
            Assert.AreEqual("DV", clienteDB.Vendedor);
        }

        [TestMethod]
        public void AplicarCambiosClienteComercial_EstadoNullDelDto_NoMachacaElEstado()
        {
            // Un llamante que no envíe estado (nullable en el DTO) no debe ponerlo a null
            var clienteDB = new Cliente { Estado = 5, Vendedor = "NV ", Usuario = "viejo" };
            var dto = new ClienteDTO { estado = null, vendedor = "NV", usuario = "carlos" };

            ClientesController.AplicarCambiosClienteComercial(clienteDB, dto);

            Assert.AreEqual((short)5, clienteDB.Estado);
            Assert.AreEqual("viejo", clienteDB.Usuario, "Sin cambios no se toca la auditoría");
        }

        // ===== Nesto#429: país fiscal editable desde la ficha comercial =====

        [TestMethod]
        public void AplicarCambiosClienteComercial_CambiaElPais_SePersisteEnMayusculasYAudita()
        {
            var clienteDB = new Cliente { Estado = 5, Vendedor = "NV ", Pais = "ES", Usuario = "viejo" };
            var dto = new ClienteDTO { estado = 5, vendedor = "NV", pais = "fr ", usuario = "carlos" };

            ClientesController.AplicarCambiosClienteComercial(clienteDB, dto);

            Assert.AreEqual("FR", clienteDB.Pais);
            Assert.AreEqual("carlos", clienteDB.Usuario);
        }

        [TestMethod]
        public void AplicarCambiosClienteComercial_PaisNull_NoLoMachaca()
        {
            // Los Nesto y NestoApp que aún no mandan el país no pueden dejarlo en blanco.
            var clienteDB = new Cliente { Estado = 5, Vendedor = "NV ", Pais = "PT", Usuario = "viejo" };
            var dto = new ClienteDTO { estado = 5, vendedor = "NV", pais = null, usuario = "carlos" };

            ClientesController.AplicarCambiosClienteComercial(clienteDB, dto);

            Assert.AreEqual("PT", clienteDB.Pais);
            Assert.AreEqual("viejo", clienteDB.Usuario, "sin cambios no se toca la auditoría");
        }

        [TestMethod]
        public void AplicarCambiosClienteComercial_MismoPaisConOtroFormato_NoEsUnCambio()
        {
            var clienteDB = new Cliente { Estado = 5, Vendedor = "NV ", Pais = "ES", Usuario = "viejo" };
            var dto = new ClienteDTO { estado = 5, vendedor = "NV", pais = "es", usuario = "carlos" };

            ClientesController.AplicarCambiosClienteComercial(clienteDB, dto);

            Assert.AreEqual("viejo", clienteDB.Usuario);
        }

        [TestMethod]
        public void EsCodigoPaisValido_SoloDosLetras()
        {
            Assert.IsTrue(ClientesController.EsCodigoPaisValido("ES"));
            Assert.IsTrue(ClientesController.EsCodigoPaisValido(" fr "));
            Assert.IsFalse(ClientesController.EsCodigoPaisValido("ESP"));
            Assert.IsFalse(ClientesController.EsCodigoPaisValido("E1"));
            Assert.IsFalse(ClientesController.EsCodigoPaisValido("España"));
        }

        // NestoAPI#327: endpoints del circuito de validación de NIF contra la AEAT

        private static ClientesController ControllerConValidacion(IServicioValidacionNif servicio)
        {
            return new ClientesController(
                A.Fake<IGestorClientes>(),
                A.Fake<IServicioVendedores>(),
                A.Fake<IGestorSincronizacion>(),
                servicio);
        }

        [TestMethod]
        public async Task CorregirNif_NifAceptado_DevuelveElResultado()
        {
            var servicio = A.Fake<IServicioValidacionNif>();
            _ = A.CallTo(() => servicio.CorregirNif("30676", "05231909H", A<string>.Ignored))
                .Returns(new ResultadoCorreccionNif { Corregido = true, Nif = "05231909H", ContactosActualizados = 2 });
            var controller = ControllerConValidacion(servicio);

            var resultado = await controller.CorregirNif(new ClientesController.CorregirNifRequest
            { Cliente = "30676", Nif = "05231909H" }) as OkNegotiatedContentResult<ResultadoCorreccionNif>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(2, resultado.Content.ContactosActualizados);
        }

        [TestMethod]
        public async Task CorregirNif_NifRechazadoPorLaAeat_BadRequestConElMotivo()
        {
            var servicio = A.Fake<IServicioValidacionNif>();
            _ = A.CallTo(() => servicio.CorregirNif(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored))
                .Returns(new ResultadoCorreccionNif { Corregido = false, Motivo = "La AEAT no reconoce el NIF" });
            var controller = ControllerConValidacion(servicio);

            var resultado = await controller.CorregirNif(new ClientesController.CorregirNifRequest
            { Cliente = "30676", Nif = "99999999R" }) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            StringAssert.Contains(resultado.Message, "AEAT");
        }

        [TestMethod]
        public async Task CorregirNif_SinClienteONif_BadRequest()
        {
            var controller = ControllerConValidacion(A.Fake<IServicioValidacionNif>());

            var sinDatos = await controller.CorregirNif(null);
            var sinNif = await controller.CorregirNif(new ClientesController.CorregirNifRequest { Cliente = "30676" });

            Assert.IsInstanceOfType(sinDatos, typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(sinNif, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task GetNifIncorrectos_VendedorNormal_FiltraPorElMismo()
        {
            var servicio = A.Fake<IServicioValidacionNif>();
            var vendedores = A.Fake<IServicioVendedores>();
            _ = A.CallTo(() => vendedores.VendedoresEquipoString("1", "JE")).Returns(new List<string>());
            _ = A.CallTo(() => servicio.ListarNifIncorrectos(A<List<string>>.That.Matches(
                    l => l != null && l.Count == 1 && l[0] == "JE")))
                .Returns(new List<ClienteNifIncorrectoDTO>
                {
                    new ClienteNifIncorrectoDTO { Cliente = "30676", Nif = "90021192", TienePedidoPendiente = true }
                });
            var controller = new ClientesController(A.Fake<IGestorClientes>(), vendedores,
                A.Fake<IGestorSincronizacion>(), servicio);

            var resultado = await controller.GetNifIncorrectos("JE") as OkNegotiatedContentResult<List<ClienteNifIncorrectoDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(1, resultado.Content.Count);
            Assert.IsTrue(resultado.Content[0].TienePedidoPendiente);
        }

        [TestMethod]
        public async Task GetNifIncorrectos_JefeDeEquipo_VeLosDeSuEquipoTambien()
        {
            // Nesto#417 (Carlos 22/07): el servidor expande el vendedor a su equipo de
            // EquiposVenta, sin que el cliente tenga que saber quién es jefe.
            var servicio = A.Fake<IServicioValidacionNif>();
            var vendedores = A.Fake<IServicioVendedores>();
            _ = A.CallTo(() => vendedores.VendedoresEquipoString("1", "ASH"))
                .Returns(new List<string> { "ASH", "DV ", "PP" });
            List<string> filtroRecibido = null;
            _ = A.CallTo(() => servicio.ListarNifIncorrectos(A<List<string>>.Ignored))
                .Invokes((List<string> l) => filtroRecibido = l)
                .Returns(new List<ClienteNifIncorrectoDTO>());
            var controller = new ClientesController(A.Fake<IGestorClientes>(), vendedores,
                A.Fake<IGestorSincronizacion>(), servicio);

            _ = await controller.GetNifIncorrectos("ASH");

            CollectionAssert.AreEquivalent(new List<string> { "ASH", "DV", "PP" }, filtroRecibido,
                "El filtro debe llevar al jefe + su equipo, sin duplicados y sin padding");
        }

        [TestMethod]
        public async Task GetNifIncorrectos_SinVendedor_NoFiltra()
        {
            var servicio = A.Fake<IServicioValidacionNif>();
            var controller = ControllerConValidacion(servicio);

            _ = await controller.GetNifIncorrectos(null);

            A.CallTo(() => servicio.ListarNifIncorrectos(null)).MustHaveHappenedOnceExactly();
        }

        // ===== Nesto#340 (slice A3): existencia del cliente principal =====
        // Agencias lo pregunta antes de contabilizar un reembolso. Lo que se prueba es el
        // CRITERIO, no el endpoint: el controlador consulta la base de datos directamente.

        private static IQueryable<Cliente> Clientes(params Cliente[] clientes) => clientes.AsQueryable();

        private static Cliente ClienteDe(string numero, bool principal, short estado) => new Cliente
        {
            Empresa = "1",
            Nº_Cliente = numero,
            ClientePrincipal = principal,
            Estado = estado
        };

        [TestMethod]
        public void PrincipalesActivos_ClientePrincipalDeAlta_LoEncuentra()
        {
            IQueryable<Cliente> clientes = Clientes(ClienteDe("22709", principal: true, estado: 0));

            Assert.IsTrue(ClientesController.PrincipalesActivos(clientes, "1", "22709").Any());
        }

        /// <summary>
        /// EL TEST QUE IMPORTA. Un cliente dado de baja (estado negativo) NO existe a estos
        /// efectos. Si dejara de filtrarse —por ejemplo reutilizando GET api/Clientes, que no
        /// filtra por estado—, Agencias contabilizaria el reembolso contra un cliente de baja
        /// sin que saltara nada.
        /// </summary>
        [TestMethod]
        public void PrincipalesActivos_ClienteDeBaja_NoCuentaComoExistente()
        {
            IQueryable<Cliente> clientes = Clientes(ClienteDe("22709", principal: true, estado: -1));

            Assert.IsFalse(ClientesController.PrincipalesActivos(clientes, "1", "22709").Any());
        }

        [TestMethod]
        public void PrincipalesActivos_SoloTieneContactosNoPrincipales_NoCuenta()
        {
            IQueryable<Cliente> clientes = Clientes(ClienteDe("22709", principal: false, estado: 0));

            Assert.IsFalse(ClientesController.PrincipalesActivos(clientes, "1", "22709").Any());
        }

        [TestMethod]
        public void PrincipalesActivos_ClienteDeOtraEmpresa_NoCuenta()
        {
            var deLaEspejo = ClienteDe("22709", principal: true, estado: 0);
            deLaEspejo.Empresa = "3";
            IQueryable<Cliente> clientes = Clientes(deLaEspejo);

            Assert.IsFalse(ClientesController.PrincipalesActivos(clientes, "1", "22709").Any());
        }

        // Nesto#486 / NestoApp#189: la API dice si la cuenta vale para el recibo, con la regla de
        // la remesa, para que Nesto y NestoApp no la repitan.

        private static CCC CuentaBancaria(string numero = "1", short estado = 0, string cuenta = "0200051332") => new CCC
        {
            Empresa = "1", Cliente = "15191", Contacto = "0", Número = numero, Estado = estado,
            Pais = "ES", DC_IBAN = "91", Entidad = "2100", Oficina = "0418", DC = "45", Nº_Cuenta = cuenta
        };

        [TestMethod]
        public void MarcarValidezParaRecibo_CuentaBuenaDeLaFicha_ValeYEsLaDeLaFicha()
        {
            var dto = new CCCDTO { numero = "1" };

            ClientesController.MarcarValidezParaRecibo(dto, CuentaBancaria(), "1  ");

            Assert.IsTrue(dto.validoParaRecibo);
            Assert.IsNull(dto.motivoNoValido);
            Assert.IsTrue(dto.esDeLaFicha);
        }

        [TestMethod]
        public void MarcarValidezParaRecibo_CuentaDeBaja_NoVale()
        {
            var dto = new CCCDTO { numero = "2" };

            ClientesController.MarcarValidezParaRecibo(dto, CuentaBancaria("2", estado: -1), "1");

            Assert.IsFalse(dto.validoParaRecibo);
            StringAssert.Contains(dto.motivoNoValido, "DE BAJA");
            Assert.IsFalse(dto.esDeLaFicha);
        }

        [TestMethod]
        public void MarcarValidezParaRecibo_IbanMal_NoVale()
        {
            var dto = new CCCDTO { numero = "3" };

            ClientesController.MarcarValidezParaRecibo(dto, CuentaBancaria("3", cuenta: "0200051333"), null);

            Assert.IsFalse(dto.validoParaRecibo);
            Assert.IsNotNull(dto.motivoNoValido);
        }

    }
}
