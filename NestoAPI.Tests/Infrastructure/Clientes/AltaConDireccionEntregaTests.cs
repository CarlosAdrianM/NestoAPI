using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Direcciones;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using NestoAPI.Models.Direcciones;
using NestoAPI.Models.Sincronizacion;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>
    /// Alta con dirección de entrega distinta (19/11/26): un cliente nuevo con dirección fiscal y otra de entrega, de una vez y en una
    /// transacción: el principal (fiscal) y un contacto de entrega al que apunta el ContactoDefecto del principal.
    /// </summary>
    [TestClass]
    public class AltaConDireccionEntregaTests
    {
        // ---------------------------------------------------------------- reglas puras

        [TestMethod]
        public void MotivoNoAdmitida_SinDireccionDeEntrega_NadaQueDecir()
        {
            Assert.IsNull(AltaConDireccionEntrega.MotivoNoAdmitida(new ClienteCrear { EsContacto = true }));
            Assert.IsNull(AltaConDireccionEntrega.MotivoNoAdmitida(null));
        }

        [TestMethod]
        public void MotivoNoAdmitida_SoloEnUnClienteNuevoYConLasDosDirecciones()
        {
            Assert.AreEqual(AltaConDireccionEntrega.MOTIVO_SOLO_CLIENTE_NUEVO,
                AltaConDireccionEntrega.MotivoNoAdmitida(Alta(contacto: true)));
            ClienteCrear sinFiscal = Alta();
            sinFiscal.Direccion = " ";
            Assert.AreEqual(AltaConDireccionEntrega.MOTIVO_SIN_DIRECCION_FISCAL, AltaConDireccionEntrega.MotivoNoAdmitida(sinFiscal));
            ClienteCrear sinEntrega = Alta();
            sinEntrega.DireccionEntrega.Direccion = null;
            Assert.AreEqual(AltaConDireccionEntrega.MOTIVO_SIN_DIRECCION, AltaConDireccionEntrega.MotivoNoAdmitida(sinEntrega));
            ClienteCrear sinCp = Alta();
            sinCp.DireccionEntrega.CodigoPostal = "";
            Assert.AreEqual(AltaConDireccionEntrega.MOTIVO_SIN_CODIGO_POSTAL, AltaConDireccionEntrega.MotivoNoAdmitida(sinCp));
            Assert.IsNull(AltaConDireccionEntrega.MotivoNoAdmitida(Alta()));
        }

        [TestMethod]
        public void ContactoDeEntrega_HeredaLoComercialYCambiaSoloLaDireccion()
        {
            ClienteCrear alta = Alta();
            alta.Comentarios = "Lo lleva Laura";
            alta.Origen = "WEB";

            ClienteCrear e = AltaConDireccionEntrega.ContactoDeEntrega(alta);

            Assert.IsTrue(e.EsContacto);
            Assert.IsNull(e.DireccionEntrega);
            Assert.AreEqual("B12345674", e.Nif);
            Assert.AreEqual("CENTRO EL EDEN SL", e.Nombre);
            Assert.AreEqual((short)5, e.Estado);
            Assert.AreEqual("RCB", e.FormaPago);
            Assert.AreEqual("1/30", e.PlazosPago);
            Assert.AreEqual("ES91 2100 0418 4502 0005 1332", e.Iban);
            Assert.AreEqual("JE", e.VendedorEstetica);
            Assert.AreEqual("01111", e.DiasEnServir);
            Assert.AreSame(alta.PersonasContacto, e.PersonasContacto, "Las personas de contacto son comunes");
            Assert.AreEqual("C/ DEL LOCAL, 6", e.Direccion);
            Assert.IsTrue(e.DireccionVerificada);
            Assert.AreEqual("Local", e.TipoLugar);
            Assert.AreEqual("EL EDÉN", e.Rotulo);
            Assert.AreEqual("28100", e.CodigoPostal);
            Assert.AreEqual("ALCOBENDAS", e.Poblacion);
            Assert.AreEqual("AT", e.Ruta);
            Assert.AreEqual("916000001", e.Telefono);
            Assert.AreEqual("Timbre 2", e.ComentariosRuta);
            Assert.AreEqual("Frágil", e.ComentariosPicking);
            Assert.IsNull(e.Comentarios, "Los comentarios generales son de la ficha fiscal");
            Assert.IsNull(e.Origen, "El origen va por cliente");
            Assert.IsNull(e.Piso);
        }

        [TestMethod]
        public void SiguienteContacto_ElDeDespuesDelPrincipal()
        {
            Assert.AreEqual("1", AltaConDireccionEntrega.SiguienteContacto("0  "));
            Assert.AreEqual("3", AltaConDireccionEntrega.SiguienteContacto("2"));
            Assert.AreEqual("1", AltaConDireccionEntrega.SiguienteContacto(null));
        }

        [TestMethod]
        public void NormalizarYValidar_TambienLaDireccionDeEntrega()
        {
            ClienteCrear alta = Alta();
            alta.DireccionEntrega.Direccion = "  C/ DEL LOCAL, 6   ";
            alta.DireccionEntrega.ComentariosRuta = new string('x', 55);

            NormalizadorClienteCrear.Normalizar(alta);

            Assert.AreEqual("C/ DEL LOCAL, 6", alta.DireccionEntrega.Direccion);
            Assert.AreEqual("El comentario de ruta de entrega no puede superar 50 caracteres (tiene 55).", MensajesValidacionCliente.Desde(alta));
        }

        // ---------------------------------------------------------------- alta

        [TestMethod]
        public async Task CrearCliente_SinDireccionDeEntrega_ComoSiempre_UnaFichaSinTransaccion()
        {
            Escenario s = new Escenario();
            ClienteCrear alta = Alta();
            alta.DireccionEntrega = null;

            Cliente creado = await s.Gestor.CrearCliente(alta, s.Db);

            Assert.AreEqual(1, s.Anadidos.Count);
            Assert.AreEqual("0", creado.Contacto);
            Assert.AreEqual("0", creado.ContactoDefecto);
            Assert.IsNull(creado.ContactoEntrega);
            Assert.AreEqual(0, s.TransaccionesAbiertas);
            Assert.AreEqual(1, s.Publicados.Count);
        }

        [TestMethod]
        public async Task CrearCliente_ConDireccionDeEntrega_DosFichasYLaEntregaPorDefecto()
        {
            Escenario s = new Escenario();

            Cliente principal = await s.Gestor.CrearCliente(Alta(), s.Db);

            Assert.AreEqual(2, s.Anadidos.Count);
            Cliente entrega = s.Anadidos[1];
            Assert.AreSame(principal, s.Anadidos[0], "Primero el principal");
            Assert.IsTrue(principal.ClientePrincipal);
            Assert.IsFalse(entrega.ClientePrincipal);
            Assert.AreEqual(principal.Nº_Cliente, entrega.Nº_Cliente);
            Assert.AreEqual("0", principal.Contacto);
            Assert.AreEqual("1", entrega.Contacto);
            Assert.AreEqual("1", principal.ContactoDefecto, "La entrega por defecto es la del contacto de entrega");
            Assert.AreEqual("0", principal.ContactoCobro);
            Assert.AreEqual("0", principal.ContactoBonificacion);
            Assert.AreEqual("1", principal.ContactoEntrega, "La respuesta dice qué contacto se ha creado");
            Assert.AreEqual("C/ FISCAL, 1", principal.Dirección);
            Assert.AreEqual("C/ DEL LOCAL, 6", entrega.Dirección);
            Assert.AreEqual("28100", entrega.CodPostal);
            Assert.AreEqual("AT", entrega.Ruta);
            Assert.AreEqual("916000001", entrega.Teléfono);
            Assert.AreEqual("Timbre 2", entrega.ComentarioRuta);
            Assert.AreEqual("Frágil", entrega.ComentarioPicking);
            Assert.IsNull(entrega.Comentarios);
            Assert.AreEqual(1, s.Commits);
            Assert.AreEqual(1, s.TransaccionesCerradas);
        }

        [TestMethod]
        public async Task CrearCliente_ConDireccionDeEntrega_ElContactoHeredaLoComercialYLasPersonas()
        {
            Escenario s = new Escenario();

            Cliente principal = await s.Gestor.CrearCliente(Alta(), s.Db);
            Cliente entrega = s.Anadidos[1];

            Assert.AreEqual(principal.CIF_NIF, entrega.CIF_NIF);
            Assert.AreEqual(principal.Nombre, entrega.Nombre);
            Assert.AreEqual(principal.Estado, entrega.Estado);
            Assert.AreEqual("JE", entrega.Vendedor);
            Assert.AreEqual(principal.DiasEnServir, entrega.DiasEnServir);
            Assert.AreEqual("RCB", entrega.CondPagoClientes.Single().FormaPago);
            Assert.AreEqual("1/30", entrega.CondPagoClientes.Single().PlazosPago);
            Assert.AreEqual(1, entrega.CCCs.Count, "Con recibo, la cuenta también");
            Assert.AreEqual(principal.CCCs.Single().Entidad, entrega.CCCs.Single().Entidad);
            CollectionAssert.AreEqual(principal.PersonasContactoClientes.Select(p => p.CorreoElectrónico).ToList(),
                entrega.PersonasContactoClientes.Select(p => p.CorreoElectrónico).ToList(), "Las personas de contacto, las mismas");
            Assert.AreEqual(2, entrega.PersonasContactoClientes.Count);
        }

        [TestMethod]
        public async Task CrearCliente_ConDireccionDeEntrega_PublicaLosDosContactos()
        {
            Escenario s = new Escenario();

            _ = await s.Gestor.CrearCliente(Alta(), s.Db);

            Assert.AreEqual(2, s.Publicados.Count);
            Assert.AreEqual("0", s.Publicados[0].Contacto);
            Assert.IsTrue(s.Publicados[0].ClientePrincipal);
            Assert.AreEqual("C/ FISCAL, 1", s.Publicados[0].Direccion);
            Assert.AreEqual("1", s.Publicados[1].Contacto);
            Assert.IsFalse(s.Publicados[1].ClientePrincipal);
            Assert.AreEqual("C/ DEL LOCAL, 6", s.Publicados[1].Direccion);
            Assert.AreEqual(2, s.Publicados[1].PersonasContacto.Count);
            A.CallTo(() => s.Servicio.LeerDireccion2Clientes(A<string>._, A<IEnumerable<string>>._)).MustHaveHappenedTwiceExactly();
        }

        [TestMethod]
        public async Task CrearCliente_LaSegundaFichaFalla_NoQuedaNingunaNiSePublica()
        {
            Escenario s = new Escenario { FallarGuardadoNumero = 3 }; // 1 principal, 2 su cuenta, 3 el contacto de entrega

            _ = await Assert.ThrowsExceptionAsync<DbUpdateException>(() => s.Gestor.CrearCliente(Alta(), s.Db));

            Assert.AreEqual(0, s.Commits, "Sin confirmar: la transacción se deshace al cerrarse");
            Assert.AreEqual(1, s.TransaccionesCerradas);
            Assert.AreEqual(0, s.Publicados.Count);
        }

        [TestMethod]
        public async Task CrearCliente_LaDireccionDeEntregaNoVale_400SinTocarNada()
        {
            Escenario s = new Escenario();
            ClienteCrear alta = Alta();
            alta.DireccionEntrega.Rotulo = " ";

            ValidationException ex = await Assert.ThrowsExceptionAsync<ValidationException>(() => s.Gestor.CrearCliente(alta, s.Db));

            Assert.AreEqual(AltaConDireccionEntrega.PREFIJO_MOTIVO + Direccion2Cliente.MOTIVO_LOCAL, ex.Message);
            Assert.AreEqual(0, s.Anadidos.Count);
            Assert.AreEqual(0, s.TransaccionesAbiertas);
        }

        [TestMethod]
        public async Task CrearCliente_NumeroDeLaEntregaQueGoogleNoEncuentra_400ConLaPregunta()
        {
            Escenario s = new Escenario(google: new DireccionDetalleDTO { CodigoPostal = "28100" });
            ClienteCrear alta = Alta();
            alta.DireccionEntrega.Numero = "6";

            ValidationException ex = await Assert.ThrowsExceptionAsync<ValidationException>(() => s.Gestor.CrearCliente(alta, s.Db));

            StringAssert.StartsWith(ex.Message, AltaConDireccionEntrega.PREFIJO_MOTIVO);
            StringAssert.Contains(ex.Message, "¿Quiere guardarlo igualmente?");
            Assert.AreEqual(0, s.Anadidos.Count);
        }

        [TestMethod]
        public async Task CrearCliente_NumeroDeLaEntregaConfirmadoSinGoogle_SeCrea()
        {
            Escenario s = new Escenario(google: new DireccionDetalleDTO { CodigoPostal = "28100" });
            ClienteCrear alta = Alta();
            alta.DireccionEntrega.Numero = "6";
            alta.DireccionEntrega.NumeroConfirmadoSinGoogle = true;

            _ = await s.Gestor.CrearCliente(alta, s.Db);

            Assert.AreEqual(2, s.Anadidos.Count);
            Assert.AreEqual(1, s.Commits);
        }

        [TestMethod]
        public async Task CrearCliente_EntregaEnUnContacto_400()
        {
            Escenario s = new Escenario();
            ClienteCrear alta = Alta(contacto: true);

            ValidationException ex = await Assert.ThrowsExceptionAsync<ValidationException>(() => s.Gestor.CrearCliente(alta, s.Db));

            Assert.AreEqual(AltaConDireccionEntrega.MOTIVO_SOLO_CLIENTE_NUEVO, ex.Message);
            Assert.AreEqual(0, s.Anadidos.Count);
        }

        // ---------------------------------------------------------------- datos

        private static ClienteCrear Alta(bool contacto = false)
        {
            return new ClienteCrear
            {
                Empresa = "1",
                Cliente = contacto ? "42143" : null,
                EsContacto = contacto,
                Nif = "B12345674",
                Nombre = "CENTRO EL EDEN SL",
                Estado = 5,
                Estetica = true,
                FormaPago = "RCB",
                PlazosPago = "1/30",
                Iban = "ES91 2100 0418 4502 0005 1332",
                VendedorEstetica = "JE",
                DiasEnServir = "01111",
                Usuario = "NUEVAVISION\\Carlos",
                Direccion = "C/ FISCAL, 1",
                DireccionVerificada = true,
                TipoLugar = "Piso",
                Piso = "1",
                Puerta = "D",
                CodigoPostal = "28001",
                Poblacion = "MADRID",
                Provincia = "MADRID",
                Ruta = "16",
                Telefono = "916000000",
                PersonasContacto = new List<PersonaContactoDTO>
                {
                    new PersonaContactoDTO { Nombre = "Laura", CorreoElectronico = "laura@eleden.es" },
                    new PersonaContactoDTO { Nombre = "Facturas", CorreoElectronico = "facturas@eleden.es", FacturacionElectronica = true }
                },
                DireccionEntrega = new DireccionEntregaCrear
                {
                    Direccion = "C/ DEL LOCAL, 6",
                    DireccionVerificada = true,
                    TipoLugar = "Local",
                    NumeroLocal = "6",
                    Rotulo = "EL EDÉN",
                    CodigoPostal = "28100",
                    Poblacion = "ALCOBENDAS",
                    Provincia = "MADRID",
                    Ruta = "AT",
                    Telefono = "916000001",
                    ComentariosRuta = "Timbre 2",
                    ComentariosPicking = "Frágil"
                }
            };
        }

        /// <summary>Gestor con la BD falsa: las fichas que se añaden, los guardados, la transacción y lo que se publica.</summary>
        private sealed class Escenario
        {
            public readonly List<Cliente> Anadidos = new List<Cliente>();
            public readonly List<ClienteSyncMessage> Publicados = new List<ClienteSyncMessage>();
            public int TransaccionesAbiertas, TransaccionesCerradas, Commits, Guardados;
            public int FallarGuardadoNumero;
            public readonly NVEntities Db;
            public readonly IServicioGestorClientes Servicio;
            public readonly GestorClientes Gestor;

            public Escenario(DireccionDetalleDTO google = null)
            {
                ColumnasOpcionales.ForzarParaTests = false; // nada fuera del EDMX: sin BD
                Db = A.Fake<NVEntities>();
                var contadores = new List<ContadorGlobal> { new ContadorGlobal { Clientes = 42200 } };
                DbSet<ContadorGlobal> setContadores = FakeDbSet(contadores);
                DbSet<Cliente> setClientes = FakeDbSet(new List<Cliente>());
                A.CallTo(() => setClientes.Add(A<Cliente>._)).ReturnsLazily((Cliente c) => { Anadidos.Add(c); return c; });
                A.CallTo(() => Db.ContadoresGlobales).Returns(setContadores);
                A.CallTo(() => Db.Clientes).Returns(setClientes);
                A.CallTo(() => Db.SaveChangesAsync()).ReturnsLazily(() =>
                {
                    Guardados++;
                    if (Guardados == FallarGuardadoNumero)
                    {
                        throw new DbUpdateException("La segunda ficha no entra");
                    }
                    return Task.FromResult(1);
                });

                Servicio = A.Fake<IServicioGestorClientes>();
                A.CallTo(() => Servicio.CalcularSiguienteContacto(A<string>._, A<string>._)).Returns(Task.FromResult("0"));
                A.CallTo(() => Servicio.VendedoresTelefonicos()).Returns(Task.FromResult(new List<string>()));
                A.CallTo(() => Servicio.BuscarCodigoPostal(A<string>._, A<string>._))
                    .ReturnsLazily((string e, string cp) => Task.FromResult(new NestoAPI.Models.CodigoPostal { Empresa = "1", Número = cp }));
                A.CallTo(() => Servicio.LeerFechasCompras(A<IEnumerable<string>>._)).Returns(Task.FromResult(new Dictionary<string, FechasComprasCliente>()));
                A.CallTo(() => Servicio.LeerDireccion2Clientes(A<string>._, A<IEnumerable<string>>._))
                    .Returns(Task.FromResult(new Dictionary<string, Direccion2Contacto>()));

                ILectorParametrosUsuario lector = A.Fake<ILectorParametrosUsuario>();
                A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, A<string>._)).Returns(null);
                IServicioDireccionesGoogle direccionesGoogle = A.Fake<IServicioDireccionesGoogle>();
                A.CallTo(() => direccionesGoogle.Geocodificar(A<string>._, A<string>._, A<string>._)).Returns(Task.FromResult(google));

                ISincronizacionEventPublisher publicador = A.Fake<ISincronizacionEventPublisher>();
                A.CallTo(() => publicador.PublishEventAsync(A<string>._, A<object>._))
                    .Invokes((string t, object m) => Publicados.Add((ClienteSyncMessage)m)).Returns(Task.CompletedTask);

                ITransaccionAltaCliente transaccion = A.Fake<ITransaccionAltaCliente>();
                A.CallTo(() => transaccion.Commit()).Invokes(() => Commits++);
                A.CallTo(() => transaccion.Dispose()).Invokes(() => TransaccionesCerradas++);

                Gestor = new GestorClientes(Servicio, A.Fake<IServicioAgencias>(), new SincronizacionEventWrapper(publicador))
                {
                    DireccionesGoogle = direccionesGoogle,
                    LectorParametros = lector,
                    AbrirTransaccionAlta = db => { TransaccionesAbiertas++; return transaccion; }
                };
            }
        }

        [TestCleanup]
        public void Limpiar() => ColumnasOpcionales.ForzarParaTests = null;

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
    }
}
