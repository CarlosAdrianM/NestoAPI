using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using NestoAPI.Tests.Helpers;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#327: validación del NIF de las fichas contra el censo de la AEAT (VNifV2),
    /// cacheada en ValidacionesNif. Reglas: la validación caduca sola si la ficha cambia de
    /// NIF/nombre; los clientes de simplificadas están excluidos; un fallo de la AEAT nunca
    /// bloquea (queda sin validar y se reintenta en el siguiente uso).
    /// </summary>
    [TestClass]
    public class ServicioValidacionNifTests
    {
        private NVEntities db;
        private DbSet<Cliente> fakeClientes;
        private DbSet<CabFacturaVta> fakeFacturas;
        private IAlmacenValidacionesNif almacen;
        private IServicioGestorClientes aeat;
        private ServicioValidacionNif servicio;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeClientes = A.Fake<DbSet<Cliente>>(o => o.Implements<IQueryable<Cliente>>().Implements<IDbAsyncEnumerable<Cliente>>());
            A.CallTo(() => db.Clientes).Returns(fakeClientes);
            // CorregirNif corrige también el NIF persistido de las facturas sin declarar
            fakeFacturas = A.Fake<DbSet<CabFacturaVta>>(o => o.Implements<IQueryable<CabFacturaVta>>().Implements<IDbAsyncEnumerable<CabFacturaVta>>());
            A.CallTo(() => db.CabsFacturasVtas).Returns(fakeFacturas);
            ConfigurarFakeDbSet(fakeFacturas, new List<CabFacturaVta>().AsQueryable());
            almacen = A.Fake<IAlmacenValidacionesNif>();
            aeat = A.Fake<IServicioGestorClientes>();
            servicio = new ServicioValidacionNif(db, almacen, aeat);
        }

        private void ConFicha(params Cliente[] fichas)
        {
            ConfigurarFakeDbSet(fakeClientes, fichas.AsQueryable());
        }

        private void ConFacturas(params CabFacturaVta[] facturas)
        {
            ConfigurarFakeDbSet(fakeFacturas, facturas.AsQueryable());
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

        private static Cliente Ficha(string cliente = "30676", string contacto = "0", string nif = "90021192",
            string nombre = "ANA ISABEL CUADRADO", bool principal = true)
        {
            return new Cliente
            {
                Empresa = "1",
                Nº_Cliente = cliente,
                Contacto = contacto,
                CIF_NIF = nif,
                Nombre = nombre,
                ClientePrincipal = principal
            };
        }

        private void AeatResponde(bool valido, string resultado)
        {
            A.CallTo(() => aeat.ComprobarNifNombre(A<string>.Ignored, A<string>.Ignored))
                .Returns(new RespuestaNifNombreCliente { NifValidado = valido, ResultadoAeat = resultado });
        }

        [TestMethod]
        public async Task ValidarSiHaceFalta_SinRegistro_PreguntaALaAeatYGuardaCorrecto()
        {
            ConFicha(Ficha(nif: "05231909H"));
            AeatResponde(valido: true, resultado: "IDENTIFICADO");

            var resultado = await servicio.ValidarSiHaceFalta("1", "30676", "0", "carlos");

            Assert.AreEqual(EstadoValidacionNif.Correcto, resultado.Estado);
            Assert.IsFalse(resultado.AcabaDeResultarIncorrecto);
            A.CallTo(() => almacen.Guardar(A<ValidacionNifRegistro>.That.Matches(r =>
                r.Estado == ServicioValidacionNif.ESTADO_CORRECTO && r.Nif == "05231909H")))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ValidarSiHaceFalta_NifNoCensado_GuardaIncorrectoYMarcaLaTransicion()
        {
            // El caso real del 21/07: NIF 90021192 (sin letra) del cliente 30676
            ConFicha(Ficha());
            AeatResponde(valido: false, resultado: "NO IDENTIFICADO");

            var resultado = await servicio.ValidarSiHaceFalta("1", "30676", "0", "carlos");

            Assert.AreEqual(EstadoValidacionNif.Incorrecto, resultado.Estado);
            Assert.IsTrue(resultado.AcabaDeResultarIncorrecto, "La transición a incorrecto es el momento del correo");
            A.CallTo(() => almacen.Guardar(A<ValidacionNifRegistro>.That.Matches(r =>
                r.Estado == ServicioValidacionNif.ESTADO_INCORRECTO)))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ValidarSiHaceFalta_YaValidadoYLaFichaNoCambio_NoVuelveAPreguntar()
        {
            ConFicha(Ficha(nif: "05231909H", nombre: "PEPA"));
            A.CallTo(() => almacen.Leer("1", "30676", "0")).Returns(new ValidacionNifRegistro
            {
                Nif = "05231909H",
                Nombre = "PEPA",
                Estado = ServicioValidacionNif.ESTADO_CORRECTO
            });

            var resultado = await servicio.ValidarSiHaceFalta("1", "30676", "0", "carlos");

            Assert.AreEqual(EstadoValidacionNif.Correcto, resultado.Estado);
            A.CallTo(() => aeat.ComprobarNifNombre(A<string>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ValidarSiHaceFalta_LaFichaCambioDeNifDespuesDeValidar_Revalida()
        {
            // La validación caduca sola: el registro guarda el NIF validado y ya no casa.
            ConFicha(Ficha(nif: "05231909H"));
            A.CallTo(() => almacen.Leer("1", "30676", "0")).Returns(new ValidacionNifRegistro
            {
                Nif = "90021192", // lo que se validó en su día (y era incorrecto)
                Nombre = "ANA ISABEL CUADRADO",
                Estado = ServicioValidacionNif.ESTADO_INCORRECTO
            });
            AeatResponde(valido: true, resultado: "IDENTIFICADO");

            var resultado = await servicio.ValidarSiHaceFalta("1", "30676", "0", "carlos");

            Assert.AreEqual(EstadoValidacionNif.Correcto, resultado.Estado);
            A.CallTo(() => aeat.ComprobarNifNombre(A<string>.Ignored, A<string>.Ignored)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ValidarSiHaceFalta_ClienteDeSimplificadas_ExcluidoSinLlamarALaAeat()
        {
            // Amazon/tienda online/público final llevan NIF ficticio a propósito y van como F2 (#325)
            ConFicha(Ficha(cliente: Constantes.ClientesEspeciales.AMAZON, nif: "NV"));

            var resultado = await servicio.ValidarSiHaceFalta("1", Constantes.ClientesEspeciales.AMAZON, "0", "carlos");

            Assert.AreEqual(EstadoValidacionNif.Excluido, resultado.Estado);
            A.CallTo(() => aeat.ComprobarNifNombre(A<string>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
            A.CallTo(() => almacen.Guardar(A<ValidacionNifRegistro>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ValidarSiHaceFalta_LaAeatNoResponde_QuedaSinValidarYNoCachea()
        {
            ConFicha(Ficha());
            A.CallTo(() => aeat.ComprobarNifNombre(A<string>.Ignored, A<string>.Ignored))
                .Throws(new System.Exception("timeout AEAT"));

            var resultado = await servicio.ValidarSiHaceFalta("1", "30676", "0", "carlos");

            Assert.AreEqual(EstadoValidacionNif.SinValidar, resultado.Estado);
            A.CallTo(() => almacen.Guardar(A<ValidacionNifRegistro>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ValidarPrincipal_UsaLaFichaDelClientePrincipal()
        {
            // Los datos fiscales de la factura salen del principal (PersistirDatosFiscalesFactura)
            ConFicha(
                Ficha(contacto: "0", nif: "11111111H", principal: false),
                Ficha(contacto: "1", nif: "05231909H", principal: true));
            AeatResponde(valido: true, resultado: "IDENTIFICADO");

            var resultado = await servicio.ValidarPrincipal("30676", "carlos");

            Assert.AreEqual(EstadoValidacionNif.Correcto, resultado.Estado);
            Assert.AreEqual("05231909H", resultado.Nif);
        }

        [TestMethod]
        public async Task ValidarSiHaceFalta_FichaSinNif_QuedaSinValidar()
        {
            ConFicha(Ficha(nif: null));

            var resultado = await servicio.ValidarSiHaceFalta("1", "30676", "0", "carlos");

            Assert.AreEqual(EstadoValidacionNif.SinValidar, resultado.Estado);
            A.CallTo(() => aeat.ComprobarNifNombre(A<string>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }

        // NestoAPI#327 / Nesto#417: corrección centralizada — "ponerlo en un sitio y se arregla todo"

        private void ConMasFakes()
        {
            var fakeModificaciones = A.Fake<DbSet<Modificacion>>(o => o.Implements<IQueryable<Modificacion>>());
            A.CallTo(() => db.Modificaciones).Returns(fakeModificaciones);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));
        }

        [TestMethod]
        public async Task CorregirNif_NifValido_LoPropagaATodosLosContactosYRegistraLaValidacion()
        {
            var principal = Ficha(contacto: "0", nif: "90021192", principal: true);
            var contacto1 = Ficha(contacto: "1", nif: "90021192", principal: false);
            ConFicha(principal, contacto1);
            ConMasFakes();
            AeatResponde(valido: true, resultado: "IDENTIFICADO");

            var resultado = await servicio.CorregirNif("30676", "90021192c", "carlos");

            Assert.IsTrue(resultado.Corregido);
            Assert.AreEqual("90021192C", resultado.Nif, "El NIF se normaliza a mayúsculas");
            Assert.AreEqual(2, resultado.ContactosActualizados);
            Assert.AreEqual("90021192C", principal.CIF_NIF, "La ficha principal debe quedar corregida");
            Assert.AreEqual("90021192C", contacto1.CIF_NIF, "Todos los contactos comparten NIF (#330)");
            A.CallTo(() => almacen.Guardar(A<ValidacionNifRegistro>.That.Matches(r =>
                r.Estado == ServicioValidacionNif.ESTADO_CORRECTO && r.Nif == "90021192C")))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task CorregirNif_LaAeatLoRechaza_NoSeTocaNada()
        {
            var principal = Ficha(nif: "90021192");
            ConFicha(principal);
            ConMasFakes();
            AeatResponde(valido: false, resultado: "NO IDENTIFICADO");

            var resultado = await servicio.CorregirNif("30676", "99999999R", "carlos");

            Assert.IsFalse(resultado.Corregido);
            StringAssert.Contains(resultado.Motivo, "No se ha modificado nada");
            Assert.AreEqual("90021192", principal.CIF_NIF, "La ficha no debe cambiar si la AEAT rechaza el NIF nuevo");
            A.CallTo(() => almacen.Guardar(A<ValidacionNifRegistro>.Ignored)).MustNotHaveHappened();
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CorregirNif_FacturasSinDeclarar_CorrigeSuNifPersistido()
        {
            // Carlos 22/07: la factura emitida lleva el NIF viejo PERSISTIDO (a Verifactu viaja
            // factura.CifNif, no la ficha): al corregir, las facturas sin declarar dentro de la
            // ventana de la sombra se corrigen también para que el job las declare bien. Las ya
            // declaradas y el histórico pre-Verifactu no se tocan.
            ConFicha(Ficha(nif: "90021192"));
            ConMasFakes();
            AeatResponde(valido: true, resultado: "IDENTIFICADO");
            System.DateTime inicio = NestoAPI.Infraestructure.Verifactu.VerifactuJobsService.FechaInicioDeclaracion;
            var sinDeclarar = new CabFacturaVta { Empresa = "1", Número = "NV2612489", Nº_Cliente = "30676", Fecha = inicio.AddDays(1), CifNif = "90021192", VerifactuUUID = null };
            var yaDeclarada = new CabFacturaVta { Empresa = "1", Número = "NV2612490", Nº_Cliente = "30676", Fecha = inicio.AddDays(1), CifNif = "90021192", VerifactuUUID = "uuid-ok" };
            var historica = new CabFacturaVta { Empresa = "1", Número = "NV2500001", Nº_Cliente = "30676", Fecha = inicio.AddDays(-30), CifNif = "90021192", VerifactuUUID = null };
            ConFacturas(sinDeclarar, yaDeclarada, historica);

            var resultado = await servicio.CorregirNif("30676", "90021192c", "carlos");

            Assert.IsTrue(resultado.Corregido);
            Assert.AreEqual(1, resultado.FacturasActualizadas);
            Assert.AreEqual("90021192C", sinDeclarar.CifNif, "La factura sin declarar debe quedar con el NIF bueno");
            Assert.AreEqual("90021192", yaDeclarada.CifNif, "Una factura ya declarada no se toca");
            Assert.AreEqual("90021192", historica.CifNif, "El histórico pre-Verifactu no se toca");
        }

        [TestMethod]
        public async Task CorregirNif_ClienteDeSimplificadas_NoSePuedeCorregir()
        {
            ConFicha(Ficha(cliente: Constantes.ClientesEspeciales.TIENDA_ONLINE));
            ConMasFakes();

            var resultado = await servicio.CorregirNif(Constantes.ClientesEspeciales.TIENDA_ONLINE, "05231909H", "carlos");

            Assert.IsFalse(resultado.Corregido);
            A.CallTo(() => aeat.ComprobarNifNombre(A<string>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ValidarSiHaceFalta_IdentificadorExtranjero_QuedaSinValidarSinLlamarALaAeat()
        {
            // NestoAPI#339: un NIF-IVA italiano jamás validará contra el censo español; sin
            // esta guarda daría falso INCORRECTO con correo al vendedor.
            ConFicha(Ficha(nif: "IT01234567890"));

            var resultado = await servicio.ValidarSiHaceFalta("1", "30676", "0", "carlos");

            Assert.AreEqual(EstadoValidacionNif.SinValidar, resultado.Estado);
            A.CallTo(() => aeat.ComprobarNifNombre(A<string>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public void EsIdentificadorExtranjero_DosLetrasInicialesSi_FormatosEspanolesNo()
        {
            Assert.IsTrue(ServicioValidacionNif.EsIdentificadorExtranjero("IT01234567890"));
            Assert.IsTrue(ServicioValidacionNif.EsIdentificadorExtranjero("FR12345678901"));
            Assert.IsFalse(ServicioValidacionNif.EsIdentificadorExtranjero("05231909H"), "DNI empieza por dígito");
            Assert.IsFalse(ServicioValidacionNif.EsIdentificadorExtranjero("X1234567L"), "NIE: una letra + dígitos");
            Assert.IsFalse(ServicioValidacionNif.EsIdentificadorExtranjero("B83455154"), "CIF: una letra + dígitos");
            Assert.IsFalse(ServicioValidacionNif.EsIdentificadorExtranjero("90021192"), "El caso real sin letra sigue validándose (y fallando)");
            // Matiz de Carlos 21/07: ES + NIF es el NIF-IVA ESPAÑOL, no un extranjero
            Assert.IsFalse(ServicioValidacionNif.EsIdentificadorExtranjero("ESB83455154"));
            Assert.IsFalse(ServicioValidacionNif.EsIdentificadorExtranjero("es05231909H"), "Insensible a mayúsculas");
        }

        [TestMethod]
        public async Task ValidarSiHaceFalta_NifIvaEspanol_ValidaContraElCensoSinElPrefijo()
        {
            // "ES" + NIF: al censo se pregunta con el NIF pelado; la ficha se queda tal cual
            ConFicha(Ficha(nif: "ESB83455154"));
            AeatResponde(valido: true, resultado: "IDENTIFICADO");

            var resultado = await servicio.ValidarSiHaceFalta("1", "30676", "0", "carlos");

            Assert.AreEqual(EstadoValidacionNif.Correcto, resultado.Estado);
            A.CallTo(() => aeat.ComprobarNifNombre("B83455154", A<string>.Ignored)).MustHaveHappenedOnceExactly();
            A.CallTo(() => almacen.Guardar(A<ValidacionNifRegistro>.That.Matches(r => r.Nif == "ESB83455154")))
                .MustHaveHappenedOnceExactly();
        }

        // NestoAPI#330: unificación automática — el NIF del principal VALIDADO se propaga a
        // los contactos con NIF distinto (a Verifactu siempre viaja el del principal, pero la
        // ficha debe quedar coherente).

        [TestMethod]
        public async Task UnificarNifContactos_PrincipalValidado_CorrigeLosContactosDesalineados()
        {
            var principal = Ficha(contacto: "0", nif: "05231909H", nombre: "PEPA", principal: true);
            var desalineado = Ficha(contacto: "1", nif: "05231909J", principal: false); // errata
            var alineado = Ficha(contacto: "2", nif: "05231909H", principal: false);
            ConFicha(principal, desalineado, alineado);
            ConMasFakes();
            A.CallTo(() => almacen.Leer("1", "30676", "0")).Returns(new ValidacionNifRegistro
            {
                Nif = "05231909H",
                Nombre = "PEPA",
                Estado = ServicioValidacionNif.ESTADO_CORRECTO
            });

            int corregidos = await servicio.UnificarNifContactos("30676", "carlos");

            Assert.AreEqual(1, corregidos);
            Assert.AreEqual("05231909H", desalineado.CIF_NIF);
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task UnificarNifContactos_PrincipalSinValidar_NoTocaNada()
        {
            // Regla de #330: nunca se extiende un dato posiblemente malo.
            var principal = Ficha(contacto: "0", nif: "05231909H", principal: true);
            var desalineado = Ficha(contacto: "1", nif: "05231909J", principal: false);
            ConFicha(principal, desalineado);
            ConMasFakes();
            // Sin registro en el almacén → el principal está sin validar

            int corregidos = await servicio.UnificarNifContactos("30676", "carlos");

            Assert.AreEqual(0, corregidos);
            Assert.AreEqual("05231909J", desalineado.CIF_NIF, "Sin veredicto de la AEAT no se propaga nada");
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }
    }
}
