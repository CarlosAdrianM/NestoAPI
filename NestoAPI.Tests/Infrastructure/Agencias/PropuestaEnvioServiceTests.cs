using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Tarifas;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#595 (slice 1): la propuesta de envío del servidor reproduce las reglas de Agencias de Nesto
    /// (AgenciasViewModel.vb). Se fakean los datos (IDatosPropuestaEnvio); el comparador es el real con tarifas reales.
    /// </summary>
    [TestClass]
    public class PropuestaEnvioServiceTests
    {
        private const string EMPRESA = "1";
        private const int PEDIDO = 927700;

        private IDatosPropuestaEnvio datos;
        private PropuestaEnvioService servicio;

        [TestInitialize]
        public void Setup()
        {
            datos = A.Fake<IDatosPropuestaEnvio>();
            A.CallTo(() => datos.LeerPedido(EMPRESA, PEDIDO)).Returns(Task.FromResult(Pedido()));
            A.CallTo(() => datos.TieneAlgunaLineaConPicking(EMPRESA, PEDIDO)).Returns(Task.FromResult(true));
            A.CallTo(() => datos.LeerEnvioPendiente(A<string>._, A<int>._)).Returns(Task.FromResult<EnviosAgencia>(null));
            A.CallTo(() => datos.LeerEnvioAmpliacion(A<string>._, A<string>._, A<string>._, A<int?>._)).Returns(Task.FromResult<EnviosAgencia>(null));
            A.CallTo(() => datos.ImporteReembolso(EMPRESA, PEDIDO)).Returns(120.50m);
            A.CallTo(() => datos.PrepagosPendientes(EMPRESA, PEDIDO)).Returns(0m);
            A.CallTo(() => datos.BultosPacking(EMPRESA, PEDIDO)).Returns(Task.FromResult(0));
            A.CallTo(() => datos.FechaPicking(A<string>._)).Returns(Task.FromResult<DateTime?>(new DateTime(2026, 10, 6)));
            A.CallTo(() => datos.LeerAgencias()).Returns(Task.FromResult(new List<AgenciaTransporte>
            {
                new AgenciaTransporte { Numero = 1, Empresa = "1  ", Nombre = "ASM" },
                new AgenciaTransporte { Numero = 5, Empresa = "3  ", Nombre = "ASM" },
                new AgenciaTransporte { Numero = 11, Empresa = "1  ", Nombre = "Canteras" },
                new AgenciaTransporte { Numero = 13, Empresa = "1  ", Nombre = "CTT" }
            }));
            ConTarifas(new TarifaGLSBusinessParcel());
            servicio = new PropuestaEnvioService(datos);
        }

        private void ConTarifas(params ITarifaAgencia[] tarifas)
        {
            var registro = A.Fake<IRegistroTarifas>();
            A.CallTo(() => registro.Todas()).Returns(tarifas);
            var fuel = A.Fake<IProveedorRecargoCombustible>();
            A.CallTo(() => fuel.RecargoCombustible(A<string>._, A<int>._)).Returns(0m);
            A.CallTo(() => datos.Comparador()).ReturnsLazily(() => new ComparadorAgencias(registro, fuel));
        }

        private static PedidoParaAgenciaDTO Pedido(string codPostal = "28001", string direccion = "Calle Mayor, 1", string vendedor = "JE ") => new PedidoParaAgenciaDTO
        {
            Empresa = "1  ",
            Numero = PEDIDO,
            Cliente = "15191     ",
            Contacto = "0  ",
            Vendedor = vendedor,
            Comentarios = "Entregar por la \"mañana\". " + new string('x', 100),
            ClienteFicha = new ClienteParaAgenciaDTO
            {
                Nombre = "PELUQUERÍA \"LA BONITA\"   ",
                Direccion = direccion + "   ",
                CodPostal = codPostal + "   ",
                Poblacion = "MADRID   ",
                Provincia = "MADRID   ",
                Telefono = "916 28 19 14 / 600 123 456",
                PersonasContacto = new List<PersonaContactoAgenciaDTO>
                {
                    new PersonaContactoAgenciaDTO { Cargo = 1, CorreoElectronico = "general@bonita.es " },
                    new PersonaContactoAgenciaDTO { Cargo = 26, CorreoElectronico = " agencia@bonita.es" }
                }
            }
        };

        // ------------------------------------------------------------ nuevo

        [TestMethod]
        public async Task Nuevo_DestinoDeLaFichaAgenciaDelComparadorYDefaultsDelPerfil()
        {
            PropuestaEnvioDTO p = await servicio.Calcular(EMPRESA, PEDIDO, null, 2.5m);

            Assert.AreEqual(PropuestaEnvioDTO.ORIGEN_NUEVO, p.Origen);
            Assert.IsNull(p.EnvioOrigen);
            Assert.AreEqual(Constantes.Agencias.AGENCIA_GLS, p.Agencia);
            Assert.AreEqual("ASM", p.AgenciaNombre);
            Assert.AreEqual("1", p.Empresa);
            Assert.AreEqual((short)96, p.Servicio, "GLS: BusinessParcel");
            Assert.AreEqual((short)18, p.Horario, "GLS: Economy");
            Assert.AreEqual((short)0, p.Retorno);
            Assert.AreEqual(34, p.Pais);
            Assert.AreEqual("ES", p.PaisIso);
            Assert.AreEqual("PELUQUERÍA LA BONITA", p.Nombre, "Sin comillas, como Nesto");
            Assert.AreEqual("PELUQUERÍA \"LA BONITA\"", p.Atencion, "Atención = el nombre tal cual (attEnvio)");
            Assert.AreEqual("Calle Mayor, 1", p.Direccion);
            Assert.AreEqual("28001", p.CodPostal);
            Assert.AreEqual("MADRID", p.Poblacion);
            Assert.AreEqual("916281914", p.Telefono);
            Assert.AreEqual("600123456", p.Movil);
            Assert.AreEqual("agencia@bonita.es", p.Email, "Primero el contacto de agencia (cargo 26)");
            Assert.AreEqual(80, p.Observaciones.Length);
            Assert.IsFalse(p.Observaciones.Contains("\""));
            Assert.AreEqual("JE", p.Vendedor);
            Assert.AreEqual(120.50m, p.Reembolso);
            Assert.AreEqual((short)1, p.Bultos, "Sin bultos ni packing de Ariadna: 1");
            Assert.AreEqual(2.5m, p.Peso);
            Assert.AreEqual(new DateTime(2026, 10, 6), p.Fecha);
            Assert.AreEqual(new DateTime(2026, 10, 7), p.FechaEntrega);
            Assert.IsTrue(p.ImporteGasto > 0m, "Coste de la agencia elegida");
            Assert.AreEqual(0, p.Avisos.Count, string.Join(" | ", p.Avisos));
        }

        [TestMethod]
        public async Task Nuevo_ElComparadorEligeLaMasBarataEntreVarias()
        {
            ConTarifas(new TarifaGLSBusinessParcel(), new TarifaCTT48h());
            ComparadorAgencias comparador = datos.Comparador();
            int esperada = comparador.MasEconomica(EMPRESA, "28001", 2.5m, 120.50m, "ES").AgenciaId;

            PropuestaEnvioDTO p = await servicio.Calcular(EMPRESA, PEDIDO, 1, 2.5m);

            Assert.AreEqual(esperada, p.Agencia);
            Assert.AreEqual(PropuestaEnvioService.DefaultsDe(esperada, "28001").Servicio, p.Servicio);
            Assert.AreEqual(comparador.CosteDeAgencia(EMPRESA, "28001", 2.5m, 120.50m, esperada).Coste, p.ImporteGasto);
        }

        [TestMethod]
        public async Task Nuevo_Canarias_CanterasConSusDefaultsYAvisoDeDimensiones()
        {
            A.CallTo(() => datos.LeerPedido(EMPRESA, PEDIDO)).Returns(Task.FromResult(Pedido("35001")));

            PropuestaEnvioDTO p = await servicio.Calcular(EMPRESA, PEDIDO, 1, 1m);

            Assert.AreEqual(Constantes.Agencias.AGENCIA_CANTERAS, p.Agencia);
            Assert.AreEqual((short)0, p.Servicio);
            Assert.AreEqual((short)0, p.Horario);
            Assert.AreEqual(34, p.Pais);
            Assert.AreEqual(0m, p.ImporteGasto, "Canteras no tiene tarifa: 0");
            Assert.IsTrue(p.Avisos.Any(a => a.Contains("dimensiones")), string.Join(" | ", p.Avisos));
        }

        [TestMethod]
        public async Task Nuevo_SinVendedor_NV()
        {
            A.CallTo(() => datos.LeerPedido(EMPRESA, PEDIDO)).Returns(Task.FromResult(Pedido(vendedor: "   ")));

            PropuestaEnvioDTO p = await servicio.Calcular(EMPRESA, PEDIDO, 1, 1m);

            Assert.AreEqual("NV", p.Vendedor);
        }

        [TestMethod]
        public async Task Bultos_LosDelPackingDeAriadnaSiNoVienen_YLosQueVienenMandan()
        {
            A.CallTo(() => datos.BultosPacking(EMPRESA, PEDIDO)).Returns(Task.FromResult(3));

            Assert.AreEqual((short)3, (await servicio.Calcular(EMPRESA, PEDIDO, null, null)).Bultos);
            Assert.AreEqual((short)2, (await servicio.Calcular(EMPRESA, PEDIDO, 2, null)).Bultos);
            Assert.AreEqual(0m, (await servicio.Calcular(EMPRESA, PEDIDO, null, null)).Peso, "Sin peso: 0");
        }

        [TestMethod]
        public async Task PedidoInexistente_Null()
        {
            A.CallTo(() => datos.LeerPedido(EMPRESA, 1)).Returns(Task.FromResult<PedidoParaAgenciaDTO>(null));

            Assert.IsNull(await servicio.Calcular(EMPRESA, 1, null, null));
        }

        // ------------------------------------------------------------ pendiente

        private static EnviosAgencia Pendiente() => new EnviosAgencia
        {
            Numero = 249500,
            Empresa = "1  ",
            Agencia = Constantes.Agencias.AGENCIA_CTT,
            Pedido = PEDIDO,
            Estado = (short)Constantes.Agencias.ESTADO_PENDIENTE,
            Servicio = 24,
            Horario = 0,
            Retorno = 0,
            Nombre = "MARÍA LÓPEZ   ",
            Direccion = "Rúa Nova 7   ",
            CodPostal = "15001",
            Poblacion = "A CORUÑA",
            Provincia = "A CORUÑA",
            Telefono = "981000000",
            Movil = "699000000",
            Email = "maria@correo.es",
            Atencion = "MARÍA",
            Observaciones = "Llamar antes",
            Vendedor = "NV",
            Fecha = new DateTime(2026, 10, 5),
            FechaEntrega = new DateTime(2026, 10, 5),
            Pais = 34,
            Reembolso = -1m,
            ImporteGasto = 0m
        };

        [TestMethod]
        public async Task ConPendiente_ElDestinoYLaAgenciaDelPendienteMandan()
        {
            ConTarifas(new TarifaGLSBusinessParcel(), new TarifaCTT48h(), new TarifaCTT24h());
            A.CallTo(() => datos.LeerEnvioPendiente(EMPRESA, PEDIDO)).Returns(Task.FromResult(Pendiente()));

            PropuestaEnvioDTO p = await servicio.Calcular(EMPRESA, PEDIDO, 2, 3m);

            Assert.AreEqual(PropuestaEnvioDTO.ORIGEN_PENDIENTE_REUTILIZADO, p.Origen);
            Assert.AreEqual(249500, p.EnvioOrigen);
            Assert.AreEqual(Constantes.Agencias.AGENCIA_CTT, p.Agencia, "La agencia es la del pendiente, no la del comparador");
            Assert.AreEqual("MARÍA LÓPEZ", p.Nombre);
            Assert.AreEqual("Rúa Nova 7", p.Direccion);
            Assert.AreEqual("15001", p.CodPostal);
            Assert.AreEqual("981000000", p.Telefono);
            Assert.AreEqual("699000000", p.Movil);
            Assert.AreEqual("maria@correo.es", p.Email);
            Assert.AreEqual("MARÍA", p.Atencion);
            Assert.AreEqual("Llamar antes", p.Observaciones);
            Assert.AreEqual((short)24, p.Servicio, "El servicio del pendiente (CTT 24h) vale: se conserva");
            Assert.AreEqual(0m, p.Reembolso, "El -1 («no cobrar») del pendiente pasa a 0");
            Assert.AreEqual((short)2, p.Bultos);
            Assert.AreEqual(3m, p.Peso);
            Assert.IsTrue(p.ImporteGasto > 0m, "Coste de CTT 24h para el destino del pendiente");
            A.CallTo(() => datos.LeerEnvioAmpliacion(A<string>._, A<string>._, A<string>._, A<int?>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ConPendiente_ServicioQueNoEsDeSuAgencia_ElDeFecto()
        {
            EnviosAgencia pendiente = Pendiente();
            pendiente.Agencia = Constantes.Agencias.AGENCIA_GLS;
            pendiente.Servicio = 48;
            pendiente.Horario = 0;
            A.CallTo(() => datos.LeerEnvioPendiente(EMPRESA, PEDIDO)).Returns(Task.FromResult(pendiente));

            PropuestaEnvioDTO p = await servicio.Calcular(EMPRESA, PEDIDO, 1, 1m);

            Assert.AreEqual((short)96, p.Servicio);
            Assert.AreEqual((short)18, p.Horario);
        }

        // ------------------------------------------------------------ ampliación

        [TestMethod]
        public async Task Ampliacion_AgenciaDelEnvioYReembolsoSumado()
        {
            var enCurso = new EnviosAgencia
            {
                Numero = 249400, Empresa = "1  ", Agencia = Constantes.Agencias.AGENCIA_CTT, Pedido = 927600,
                Estado = (short)Constantes.Agencias.ESTADO_EN_CURSO, Reembolso = 30m, Direccion = "Calle Mayor, 1"
            };
            A.CallTo(() => datos.LeerEnvioAmpliacion("15191     ", "0  ", "Calle Mayor, 1", 77)).Returns(Task.FromResult(enCurso));

            PropuestaEnvioDTO p = await servicio.Calcular(EMPRESA, PEDIDO, 3, 1m, excluirEnvio: 77);

            Assert.AreEqual(PropuestaEnvioDTO.ORIGEN_AMPLIACION, p.Origen);
            Assert.AreEqual(249400, p.EnvioOrigen);
            Assert.AreEqual(Constantes.Agencias.AGENCIA_CTT, p.Agencia);
            Assert.AreEqual(150.50m, p.Reembolso, "30 del envío + 120,50 de este pedido");
            Assert.IsTrue(p.Avisos.Any(a => a.Contains("ampliación")), string.Join(" | ", p.Avisos));
        }

        // ------------------------------------------------------------ avisos

        [TestMethod]
        public async Task Avisos_SinPickingYPrepagos()
        {
            A.CallTo(() => datos.TieneAlgunaLineaConPicking(EMPRESA, PEDIDO)).Returns(Task.FromResult(false));
            A.CallTo(() => datos.PrepagosPendientes(EMPRESA, PEDIDO)).Returns(50m);

            PropuestaEnvioDTO p = await servicio.Calcular(EMPRESA, PEDIDO, 1, 1m);

            Assert.AreEqual(120.50m, p.Reembolso, "#569: el reembolso NO descuenta los prepagos");
            Assert.AreEqual(50m, p.PrepagosPendientes);
            Assert.IsTrue(p.Avisos.Contains("Este pedido no tiene ninguna línea con picking."), string.Join(" | ", p.Avisos));
            string avisoPrepagos = p.Avisos.SingleOrDefault(a => a.Contains("pagados por adelantado"));
            Assert.IsNotNull(avisoPrepagos, string.Join(" | ", p.Avisos));
            StringAssert.Contains(avisoPrepagos, "50,00");
            StringAssert.Contains(avisoPrepagos, "120,50");
        }

        [TestMethod]
        public async Task Avisos_PrepagosSinReembolso_NoAvisa()
        {
            A.CallTo(() => datos.ImporteReembolso(EMPRESA, PEDIDO)).Returns(0m);
            A.CallTo(() => datos.PrepagosPendientes(EMPRESA, PEDIDO)).Returns(50m);

            PropuestaEnvioDTO p = await servicio.Calcular(EMPRESA, PEDIDO, 1, 1m);

            Assert.IsFalse(p.Avisos.Any(a => a.Contains("pagados por adelantado")));
        }

        [TestMethod]
        public async Task Avisos_DireccionPropia()
        {
            A.CallTo(() => datos.LeerPedido(EMPRESA, PEDIDO)).Returns(Task.FromResult(Pedido("28110", "C/ Río Tiétar, 11")));

            PropuestaEnvioDTO p = await servicio.Calcular(EMPRESA, PEDIDO, 1, 1m);

            Assert.IsTrue(p.Avisos.Any(a => a.Contains("Nueva Visión")), string.Join(" | ", p.Avisos));
        }

        [TestMethod]
        public async Task Prepagos_SiFallaLaLectura_SinAvisoYSigue()
        {
            A.CallTo(() => datos.PrepagosPendientes(EMPRESA, PEDIDO)).Throws(new InvalidOperationException("caída"));

            PropuestaEnvioDTO p = await servicio.Calcular(EMPRESA, PEDIDO, 1, 1m);

            Assert.AreEqual(0m, p.PrepagosPendientes);
        }

        // ------------------------------------------------------------ piezas

        [TestMethod]
        public void AgenciaDeLaEmpresa_EnLaEspejoBuscaLaMismaAgenciaPorNombre()
        {
            var agencias = new List<AgenciaTransporte>
            {
                new AgenciaTransporte { Numero = 1, Empresa = "1  ", Nombre = "ASM" },
                new AgenciaTransporte { Numero = 5, Empresa = "3  ", Nombre = "ASM  " }
            };

            Assert.AreEqual(5, PropuestaEnvioService.AgenciaDeLaEmpresa(1, "3", agencias));
            Assert.AreEqual(1, PropuestaEnvioService.AgenciaDeLaEmpresa(1, "1", agencias));
        }

        [TestMethod]
        public void PaisIsoDe_PortugalPorPaisOPorCodigoPostal()
        {
            Assert.AreEqual("PT", PropuestaEnvioService.PaisIsoDe(351, "28001"));
            Assert.AreEqual("PT", PropuestaEnvioService.PaisIsoDe(0, "4000-123"));
            Assert.AreEqual("ES", PropuestaEnvioService.PaisIsoDe(34, "28001"));
        }

        [TestMethod]
        public void ServicioForzadoParaCoste_SoloSiNoEsElDefectoDeUnaAgenciaDeLaApi()
        {
            Assert.IsNull(PropuestaEnvioService.ServicioForzadoParaCoste(Constantes.Agencias.AGENCIA_CTT, 48, "28001"));
            Assert.AreEqual((byte)24, PropuestaEnvioService.ServicioForzadoParaCoste(Constantes.Agencias.AGENCIA_CTT, 24, "28001"));
            Assert.IsNull(PropuestaEnvioService.ServicioForzadoParaCoste(Constantes.Agencias.AGENCIA_GLS, 6, "07001"));
        }

        // ------------------------------------------------------------ comparación de la sombra

        private static EnviosAgencia EnvioIgualA(PropuestaEnvioDTO p) => new EnviosAgencia
        {
            Numero = 250000, Pedido = p.Pedido, Agencia = p.Agencia, Servicio = p.Servicio, Horario = p.Horario, Retorno = p.Retorno,
            Reembolso = p.Reembolso, Nombre = p.Nombre + "   ", Direccion = p.Direccion, CodPostal = p.CodPostal, Poblacion = p.Poblacion,
            Provincia = p.Provincia, Pais = p.Pais, Telefono = p.Telefono, Movil = p.Movil, Email = p.Email, Atencion = p.Atencion,
            Vendedor = p.Vendedor + " ", Bultos = p.Bultos, Peso = p.Peso, ImporteGasto = p.ImporteGasto + 0.004m
        };

        [TestMethod]
        public async Task Diferencias_SiCoincideNoHayNinguna()
        {
            PropuestaEnvioDTO p = await servicio.Calcular(EMPRESA, PEDIDO, 1, 2m);

            CollectionAssert.AreEqual(new List<string>(), ComparadorPropuestaEnvio.Diferencias(EnvioIgualA(p), p));
        }

        [TestMethod]
        public async Task Diferencias_ListaCadaCampoConNestoYPropuesta()
        {
            PropuestaEnvioDTO p = await servicio.Calcular(EMPRESA, PEDIDO, 1, 2m);
            EnviosAgencia envio = EnvioIgualA(p);
            envio.Agencia = Constantes.Agencias.AGENCIA_CTT;
            envio.CodPostal = "28002";
            envio.ImporteGasto = p.ImporteGasto + 0.5m;

            List<string> diferencias = ComparadorPropuestaEnvio.Diferencias(envio, p);

            Assert.AreEqual(3, diferencias.Count, string.Join(" | ", diferencias));
            CollectionAssert.Contains(diferencias, "agencia: Nesto=13 / propuesta=1");
            CollectionAssert.Contains(diferencias, "CP: Nesto=28002 / propuesta=28001");
            Assert.IsTrue(diferencias.Any(d => d.StartsWith("ImporteGasto:")));
        }
    }
}
