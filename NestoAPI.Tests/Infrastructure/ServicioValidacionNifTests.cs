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
            string nombre = "ANA ISABEL CUADRADO", bool principal = true, string pais = null)
        {
            return new Cliente
            {
                Empresa = "1",
                Nº_Cliente = cliente,
                Contacto = contacto,
                CIF_NIF = nif,
                Nombre = nombre,
                ClientePrincipal = principal,
                Pais = pais
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
        public async Task CorregirNif_FacturasExcluidasDelJob_LasReabre()
        {
            // Fallo 20/08/26 (cliente 9093 de Amparo): las facturas marcadas NO CENSADO con NIF
            // de relleno quedan EXCLUIDAS del job (VerifactuEstado="SinDatosFiscales"). Al llegar
            // el DNI real, CorregirNif debe REABRIRLAS (VerifactuEstado null) — antes solo
            // corregía CifNif/NombreFiscal y seguían excluidas para siempre. La reapertura aplica
            // también si el NIF ya estaba bien: una factura sin declarar con estado informado es
            // una exclusión o un rechazo, nunca una declaración en curso (esas tienen UUID).
            ConFicha(Ficha(cliente: "9093", nif: "1000000", nombre: "AMPARO CORELLA RUBIO"));
            ConMasFakes();
            AeatResponde(valido: true, resultado: "IDENTIFICADO");
            System.DateTime inicio = NestoAPI.Infraestructure.Verifactu.VerifactuJobsService.FechaInicioDeclaracion;
            var excluida = new CabFacturaVta { Empresa = "1", Número = "NV2613367", Nº_Cliente = "9093", Fecha = inicio.AddDays(1), CifNif = "1000000", VerifactuEstado = "SinDatosFiscales", VerifactuUUID = null };
            var excluidaConNifBueno = new CabFacturaVta { Empresa = "1", Número = "NV2613965", Nº_Cliente = "9093", Fecha = inicio.AddDays(1), CifNif = "12345678Z", NombreFiscal = "AMPARO CORELLA RUBIO", VerifactuEstado = "SinDatosFiscales", VerifactuUUID = null };
            var yaDeclarada = new CabFacturaVta { Empresa = "1", Número = "NV2613999", Nº_Cliente = "9093", Fecha = inicio.AddDays(1), CifNif = "1000000", VerifactuEstado = "Correcto", VerifactuUUID = "uuid-ok" };
            ConFacturas(excluida, excluidaConNifBueno, yaDeclarada);

            var resultado = await servicio.CorregirNif("9093", "12345678Z", "carlos");

            Assert.IsTrue(resultado.Corregido);
            Assert.AreEqual("12345678Z", excluida.CifNif);
            Assert.IsNull(excluida.VerifactuEstado, "La factura excluida se reabre para que el job la declare");
            Assert.IsNull(excluidaConNifBueno.VerifactuEstado, "También se reabre aunque su NIF ya estuviera bien");
            Assert.AreEqual("Correcto", yaDeclarada.VerifactuEstado, "Una factura ya declarada no se toca");
            Assert.AreEqual(2, resultado.FacturasActualizadas);
        }

        // NestoAPI#383 (caso real NV2612562/NV2612940): el rechazo de censo puede ser por el
        // NOMBRE y no por el NIF (cambio de apellido por matrimonio). El circuito #327 propagaba
        // el NIF corregido a las facturas sin declarar pero NO el nombre: quedaban atascadas
        // para siempre reintentándose con el apellido de soltera.

        [TestMethod]
        public async Task CorregirNif_FacturasSinDeclarar_PropagaTambienElNombreFiscal()
        {
            ConFicha(Ficha(cliente: "26760", nif: "60243388", nombre: "DEREN KIDASUK ZHANNA"));
            ConMasFakes();
            AeatResponde(valido: true, resultado: "IDENTIFICADO");
            System.DateTime inicio = NestoAPI.Infraestructure.Verifactu.VerifactuJobsService.FechaInicioDeclaracion;
            var sinDeclarar = new CabFacturaVta { Empresa = "1", Número = "NV2612562", Nº_Cliente = "26760", Fecha = inicio.AddDays(1), CifNif = "60243388", NombreFiscal = "ZHANNA YURCHYK", VerifactuUUID = null };
            ConFacturas(sinDeclarar);

            var resultado = await servicio.CorregirNif("26760", "60243388V", "carlos");

            Assert.IsTrue(resultado.Corregido);
            Assert.AreEqual("60243388V", sinDeclarar.CifNif);
            Assert.AreEqual("DEREN KIDASUK ZHANNA", sinDeclarar.NombreFiscal,
                "El nombre fiscal persistido debe corregirse junto al NIF (el mapeador declara factura.NombreFiscal)");
        }

        [TestMethod]
        public async Task CorregirNombreFiscalFactura_OtraFacturaAceptadaConElMismoNif_AdoptaSuNombre()
        {
            // La ficha del cliente sigue con el apellido de soltera (crearon un cliente NUEVO en
            // vez de renombrarla), pero la factura nueva del otro cliente (mismo NIF) ya está
            // aceptada por la AEAT: su nombre es el censal y es el candidato que desatasca.
            ConFicha(Ficha(cliente: "26760", nif: "60243388V", nombre: "ZHANNA YURCHYK"));
            ConMasFakes();
            var atascada = new CabFacturaVta { Empresa = "1", Número = "NV2612562", Nº_Cliente = "26760", CifNif = "60243388V", NombreFiscal = "ZHANNA YURCHYK" };
            var aceptada = new CabFacturaVta { Empresa = "1", Número = "NV2612941", Nº_Cliente = "41791", CifNif = "60243388V", NombreFiscal = "DEREN KIDASUK ZHANNA", VerifactuUUID = "uuid-ok", VerifactuEstado = "Correcto" };
            ConFacturas(atascada, aceptada);
            A.CallTo(() => aeat.ComprobarNifNombre("60243388V", "DEREN KIDASUK ZHANNA"))
                .Returns(new RespuestaNifNombreCliente { NifValidado = true, ResultadoAeat = "IDENTIFICADO", NombreFormateado = "DEREN KIDASUK ZHANNA" });

            bool corregido = await servicio.CorregirNombreFiscalFactura(atascada, "VerifactuJob");

            Assert.IsTrue(corregido);
            Assert.AreEqual("DEREN KIDASUK ZHANNA", atascada.NombreFiscal);
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();
        }

        [TestMethod]
        public async Task CorregirNombreFiscalFactura_FichaRenombrada_EsElPrimerCandidato()
        {
            // El flujo previsto por #329 ("corrige la ficha y se declara sola") por fin funciona
            // también para el nombre: la ficha renombrada es el primer candidato.
            ConFicha(Ficha(cliente: "26760", nif: "60243388V", nombre: "DEREN KIDASUK ZHANNA"));
            ConMasFakes();
            var atascada = new CabFacturaVta { Empresa = "1", Número = "NV2612562", Nº_Cliente = "26760", CifNif = "60243388V", NombreFiscal = "ZHANNA YURCHYK" };
            ConFacturas(atascada);
            // La AEAT identifica pero sin devolver nombre formateado: se adopta el candidato
            A.CallTo(() => aeat.ComprobarNifNombre("60243388V", "DEREN KIDASUK ZHANNA"))
                .Returns(new RespuestaNifNombreCliente { NifValidado = true, ResultadoAeat = "IDENTIFICADO" });

            bool corregido = await servicio.CorregirNombreFiscalFactura(atascada, "VerifactuJob");

            Assert.IsTrue(corregido);
            Assert.AreEqual("DEREN KIDASUK ZHANNA", atascada.NombreFiscal);
        }

        [TestMethod]
        public async Task CorregirNombreFiscalFactura_LaAeatDevuelveSimilar_AdoptaElNombreCensalDeLaAeat()
        {
            // NO IDENTIFICADO-SIMILAR: la AEAT reconoce el NIF con un nombre parecido y devuelve
            // el censal EXACTO en nombreDevuelto — ese es el que pasa el filtro de Verifacti.
            ConFicha(Ficha(cliente: "26760", nif: "60243388V", nombre: "DEREN KIDASUK"));
            ConMasFakes();
            var atascada = new CabFacturaVta { Empresa = "1", Número = "NV2612562", Nº_Cliente = "26760", CifNif = "60243388V", NombreFiscal = "ZHANNA YURCHYK" };
            ConFacturas(atascada);
            A.CallTo(() => aeat.ComprobarNifNombre("60243388V", "DEREN KIDASUK"))
                .Returns(new RespuestaNifNombreCliente { NifValidado = true, ResultadoAeat = "NO IDENTIFICADO-SIMILAR", NombreFormateado = "DEREN KIDASUK ZHANNA" });

            bool corregido = await servicio.CorregirNombreFiscalFactura(atascada, "VerifactuJob");

            Assert.IsTrue(corregido);
            Assert.AreEqual("DEREN KIDASUK ZHANNA", atascada.NombreFiscal, "Debe adoptarse el nombre censal de la AEAT, no el candidato aproximado");
        }

        [TestMethod]
        public async Task CorregirNombreFiscalFactura_SinCandidatoQueIdentifique_NoTocaNada()
        {
            ConFicha(Ficha(cliente: "26760", nif: "60243388V", nombre: "OTRO NOMBRE CUALQUIERA"));
            ConMasFakes();
            var atascada = new CabFacturaVta { Empresa = "1", Número = "NV2612562", Nº_Cliente = "26760", CifNif = "60243388V", NombreFiscal = "ZHANNA YURCHYK" };
            ConFacturas(atascada);
            AeatResponde(valido: false, resultado: "NO IDENTIFICADO");

            bool corregido = await servicio.CorregirNombreFiscalFactura(atascada, "VerifactuJob");

            Assert.IsFalse(corregido);
            Assert.AreEqual("ZHANNA YURCHYK", atascada.NombreFiscal, "Sin candidato censal no se toca nada");
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CorregirNombreFiscalFactura_ResultadoConAviso_NoSeAdopta()
        {
            // IDENTIFICADO-BAJA/REVOCADO: NifValidado es true pero el nombre devuelto lleva el
            // prefijo de aviso ("¡EMPRESA DE BAJA! ..."): no es un nombre censal utilizable.
            ConFicha(Ficha(cliente: "26760", nif: "60243388V", nombre: "DEREN KIDASUK ZHANNA"));
            ConMasFakes();
            var atascada = new CabFacturaVta { Empresa = "1", Número = "NV2612562", Nº_Cliente = "26760", CifNif = "60243388V", NombreFiscal = "ZHANNA YURCHYK" };
            ConFacturas(atascada);
            A.CallTo(() => aeat.ComprobarNifNombre(A<string>.Ignored, A<string>.Ignored))
                .Returns(new RespuestaNifNombreCliente { NifValidado = true, ResultadoAeat = "IDENTIFICADO-BAJA", NombreFormateado = "¡EMPRESA DE BAJA! DEREN KIDASUK ZHANNA" });

            bool corregido = await servicio.CorregirNombreFiscalFactura(atascada, "VerifactuJob");

            Assert.IsFalse(corregido);
            Assert.AreEqual("ZHANNA YURCHYK", atascada.NombreFiscal);
        }

        [TestMethod]
        public async Task MarcarIdentificacionExtranjera_GuardaElRegistroConTipoYPais()
        {
            // NestoAPI#339: un pasaporte jamás validará contra el censo — se marca a mano y
            // las facturas pasan a declararse con IDOtro (tipo L7 + país).
            ConFicha(Ficha(nif: "AB123456"));
            ConMasFakes();

            var resultado = await servicio.MarcarIdentificacionExtranjera("30676", "03", "ma", "carlos");

            Assert.IsTrue(resultado.Corregido);
            A.CallTo(() => almacen.Guardar(A<ValidacionNifRegistro>.That.Matches(r =>
                r.Estado == ServicioValidacionNif.ESTADO_EXTRANJERO
                && r.TipoIdentificacion == "03" && r.Pais == "MA" && r.Nif == "AB123456")))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => aeat.ComprobarNifNombre(A<string>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }

        // NestoAPI#356/#354: el NIF-IVA extranjero se truncaba a 9 chars (char(9)); ahora "marcar
        // extranjero" acepta el NIF completo y lo propaga a fichas y facturas sin declarar.

        [TestMethod]
        public async Task MarcarIdentificacionExtranjera_ConNifNuevo_PropagaAFichaYFacturasSinValidarAeat()
        {
            Cliente ficha = Ficha(cliente: "41777", nif: "IT0280027", nombre: "RPF SRL"); // truncado a 9
            ConFicha(ficha);
            ConMasFakes();
            System.DateTime inicio = NestoAPI.Infraestructure.Verifactu.VerifactuJobsService.FechaInicioDeclaracion;
            var sinDeclarar = new CabFacturaVta { Empresa = "1", Número = "NV2612580", Nº_Cliente = "41777", Fecha = inicio.AddDays(1), CifNif = "IT0280027", VerifactuUUID = null, VerifactuEstado = "SinDatosFiscales" };
            ConFacturas(sinDeclarar);

            var resultado = await servicio.MarcarIdentificacionExtranjera("41777", "02", "it", "carlos", "IT01579720287");

            Assert.IsTrue(resultado.Corregido);
            Assert.AreEqual("IT01579720287", ficha.CIF_NIF, "La ficha queda con el NIF-IVA completo");
            Assert.AreEqual("IT01579720287", sinDeclarar.CifNif, "La factura sin declarar queda con el NIF-IVA completo");
            Assert.IsNull(sinDeclarar.VerifactuEstado, "La factura excluida se reabre para reintentarla");
            Assert.AreEqual(1, resultado.FacturasActualizadas);
            // La marca extranjera NUNCA valida contra el censo (un NIF-IVA no está en la AEAT española).
            A.CallTo(() => aeat.ComprobarNifNombre(A<string>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
            A.CallTo(() => almacen.Guardar(A<ValidacionNifRegistro>.That.Matches(r =>
                r.Estado == ServicioValidacionNif.ESTADO_EXTRANJERO
                && r.TipoIdentificacion == "02" && r.Pais == "IT" && r.Nif == "IT01579720287")))
                .MustHaveHappenedOnceExactly();
        }

        // NestoAPI#354: el país fiscal de la UE (≠ES) declara con IDOtro tipo 02 automáticamente,
        // sin marca manual ni censo español (Clientes.Pais es la fuente de verdad).

        [TestMethod]
        public void EsPaisUnionEuropeaDistintoDeEspana_SoloUEDistintaDeEspana()
        {
            Assert.IsTrue(ServicioValidacionNif.EsPaisUnionEuropeaDistintoDeEspana("IT"));
            Assert.IsTrue(ServicioValidacionNif.EsPaisUnionEuropeaDistintoDeEspana("fr"));
            Assert.IsFalse(ServicioValidacionNif.EsPaisUnionEuropeaDistintoDeEspana("ES"), "España no");
            Assert.IsFalse(ServicioValidacionNif.EsPaisUnionEuropeaDistintoDeEspana("GB"), "Reino Unido ya no es UE");
            Assert.IsFalse(ServicioValidacionNif.EsPaisUnionEuropeaDistintoDeEspana("MA"), "Marruecos no es UE");
            Assert.IsFalse(ServicioValidacionNif.EsPaisUnionEuropeaDistintoDeEspana(null));
        }

        [TestMethod]
        public async Task ValidarPrincipal_PaisUEDistintoDeEspana_ExtranjeroTipo02SinAeat()
        {
            ConFicha(Ficha(cliente: "41777", nif: "IT01579720287", nombre: "RPF SRL", pais: "IT"));
            ConMasFakes();
            A.CallTo(() => almacen.Leer(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored))
                .Returns(Task.FromResult<ValidacionNifRegistro>(null)); // sin marca previa

            var resultado = await servicio.ValidarPrincipal("41777", "Verifactu");

            Assert.AreEqual(EstadoValidacionNif.Extranjero, resultado.Estado);
            Assert.AreEqual("02", resultado.TipoIdentificacion, "NIF-IVA intracomunitario");
            Assert.AreEqual("IT", resultado.Pais);
            A.CallTo(() => aeat.ComprobarNifNombre(A<string>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ValidarPrincipal_PaisES_SiValidaContraElCenso()
        {
            ConFicha(Ficha(cliente: "30676", nif: "90021192C", nombre: "ANA", pais: "ES"));
            ConMasFakes();
            A.CallTo(() => almacen.Leer(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored))
                .Returns(Task.FromResult<ValidacionNifRegistro>(null));
            AeatResponde(valido: true, resultado: "IDENTIFICADO");

            var resultado = await servicio.ValidarPrincipal("30676", "Verifactu");

            Assert.AreEqual(EstadoValidacionNif.Correcto, resultado.Estado, "Un cliente ES sí pasa por el censo");
            A.CallTo(() => aeat.ComprobarNifNombre(A<string>.Ignored, A<string>.Ignored)).MustHaveHappened();
        }

        [TestMethod]
        public async Task ValidarPrincipal_PaisNoUE_NoAutoExtranjero()
        {
            // Marruecos (no UE): el tipo es ambiguo (pasaporte, doc. país...), se deja a la marca manual.
            ConFicha(Ficha(cliente: "50000", nif: "X1234567L", nombre: "CLIENTE MA", pais: "MA"));
            ConMasFakes();
            A.CallTo(() => almacen.Leer(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored))
                .Returns(Task.FromResult<ValidacionNifRegistro>(null));

            var resultado = await servicio.ValidarPrincipal("50000", "Verifactu");

            Assert.AreNotEqual(EstadoValidacionNif.Extranjero, resultado.Estado,
                "Un país no-UE no se auto-marca (tipo ambiguo): queda para la marca manual");
        }

        [TestMethod]
        public async Task MarcarIdentificacionExtranjera_FijaElPaisFiscalDeLaFicha()
        {
            Cliente ficha = Ficha(cliente: "41777", nif: "IT01579720287", pais: "ES");
            ConFicha(ficha);
            ConMasFakes();

            await servicio.MarcarIdentificacionExtranjera("41777", "02", "it", "carlos");

            Assert.AreEqual("IT", ficha.Pais, "Marcar extranjero deja también el país fiscal en la ficha");
        }

        [TestMethod]
        public async Task MarcarIdentificacionExtranjera_SinNifNuevo_NoTocaFichaNiFacturas()
        {
            Cliente ficha = Ficha(cliente: "41777", nif: "IT0280027");
            ConFicha(ficha);
            ConMasFakes();
            var factura = new CabFacturaVta { Empresa = "1", Número = "NV2612580", Nº_Cliente = "41777", Fecha = NestoAPI.Infraestructure.Verifactu.VerifactuJobsService.FechaInicioDeclaracion.AddDays(1), CifNif = "IT0280027", VerifactuUUID = null };
            ConFacturas(factura);

            var resultado = await servicio.MarcarIdentificacionExtranjera("41777", "02", "it", "carlos");

            Assert.IsTrue(resultado.Corregido);
            Assert.AreEqual("IT0280027", ficha.CIF_NIF, "Sin NIF nuevo la ficha no se toca");
            Assert.AreEqual("IT0280027", factura.CifNif, "Sin NIF nuevo la factura no se toca");
            Assert.AreEqual(0, resultado.FacturasActualizadas);
        }

        // NestoAPI#391: tipo 07 "no censado" para clientes ESPAÑOLES cuyo NIF no esté censado.
        // Reutiliza el circuito de la marca extranjera: IDOtro no se valida contra el censo.
        // Fallo 20/08/26 (cliente 9093 de Amparo): la AEAT SÍ valida que el ID del tipo 07
        // tenga FORMATO de NIF ("El campo id_otro.id no tiene un formato válido" en cada
        // reintento del job con el relleno "1000000"), así que la marca exige NIF bien formado.

        [TestMethod]
        public async Task MarcarIdentificacionExtranjera_Tipo07Espana_MarcaNoCensadoSinValidarCenso()
        {
            // NIF con formato válido (letra de control correcta) que la AEAT no tiene censado
            ConFicha(Ficha(cliente: "9093", nif: "12345678Z", nombre: "AMPARO CORELLA RUBIO"));
            ConMasFakes();

            var resultado = await servicio.MarcarIdentificacionExtranjera("9093",
                ServicioValidacionNif.TIPO_NO_CENSADO, "es", "carlos");

            Assert.IsTrue(resultado.Corregido);
            Assert.IsTrue(resultado.Motivo.Contains("NO CENSADO"),
                "El mensaje no debe hablar de 'extranjera' para un cliente español no censado");
            A.CallTo(() => almacen.Guardar(A<ValidacionNifRegistro>.That.Matches(r =>
                r.Estado == ServicioValidacionNif.ESTADO_EXTRANJERO
                && r.TipoIdentificacion == "07" && r.Pais == "ES" && r.Nif == "12345678Z")))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => aeat.ComprobarNifNombre(A<string>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task MarcarIdentificacionExtranjera_Tipo07ConNifDeRelleno_SeRechazaConElMotivo()
        {
            // El caso real de Amparo (20/08/26): NV2613367/NV2613965 del cliente 9093 con NIF
            // "1000000". La marca se guardaba y Verifacti rechazaba el alta en cada pasada.
            ConFicha(Ficha(cliente: "9093", nif: "1000000", nombre: "AMPARO CORELLA RUBIO"));
            ConMasFakes();

            var resultado = await servicio.MarcarIdentificacionExtranjera("9093",
                ServicioValidacionNif.TIPO_NO_CENSADO, "es", "carlos");

            Assert.IsFalse(resultado.Corregido);
            Assert.IsTrue(resultado.Motivo.Contains("formato"),
                "El motivo debe explicar que la AEAT exige un NIF con formato válido");
            A.CallTo(() => almacen.Guardar(A<ValidacionNifRegistro>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task MarcarIdentificacionExtranjera_Tipo07ConPaisNoEspanol_SeRechaza()
        {
            ConFicha(Ficha(cliente: "9093", nif: "12345678Z"));
            ConMasFakes();

            var resultado = await servicio.MarcarIdentificacionExtranjera("9093",
                ServicioValidacionNif.TIPO_NO_CENSADO, "fr", "carlos");

            Assert.IsFalse(resultado.Corregido);
            A.CallTo(() => almacen.Guardar(A<ValidacionNifRegistro>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task MarcarIdentificacionExtranjera_PaisEspanaConTipoQueNoAdmiteLaAeat_SeRechaza()
        {
            // AEAT error 1233: con CodigoPais ES el IDType solo puede ser 03 (pasaporte) o 07
            ConFicha(Ficha());
            ConMasFakes();

            var resultado = await servicio.MarcarIdentificacionExtranjera("30676", "06", "es", "carlos");

            Assert.IsFalse(resultado.Corregido);
            Assert.IsTrue(resultado.Motivo.Contains("1233"), "El motivo debe citar la validación de la AEAT");
            A.CallTo(() => almacen.Guardar(A<ValidacionNifRegistro>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public void TieneFormatoNif_ValidaDniNieYCifConSuControl()
        {
            // DNI: letra = número % 23 sobre TRWAGMYFPDXBNJZSQVHLCKE
            Assert.IsTrue(ServicioValidacionNif.TieneFormatoNif("12345678Z"));
            Assert.IsTrue(ServicioValidacionNif.TieneFormatoNif("53444788X"));
            Assert.IsFalse(ServicioValidacionNif.TieneFormatoNif("12345678A"), "Letra de control incorrecta");
            // NIE: X/Y/Z + 7 dígitos + letra (X=0 delante)
            Assert.IsTrue(ServicioValidacionNif.TieneFormatoNif("X1234567L"));
            Assert.IsFalse(ServicioValidacionNif.TieneFormatoNif("X1234567T"), "Letra de control incorrecta");
            // CIF: letra de organización + 7 dígitos + control (dígito o letra equivalente)
            Assert.IsTrue(ServicioValidacionNif.TieneFormatoNif("A58818501"));
            Assert.IsTrue(ServicioValidacionNif.TieneFormatoNif("A5881850A"), "La letra equivalente al dígito también vale");
            Assert.IsFalse(ServicioValidacionNif.TieneFormatoNif("A58818502"), "Dígito de control incorrecto");
            // Rellenos y basura: lo que provocó el fallo del 20/08/26
            Assert.IsFalse(ServicioValidacionNif.TieneFormatoNif("1000000"));
            Assert.IsFalse(ServicioValidacionNif.TieneFormatoNif(""));
            Assert.IsFalse(ServicioValidacionNif.TieneFormatoNif(null));
            Assert.IsFalse(ServicioValidacionNif.TieneFormatoNif("123456789"));
            // Normaliza espacios y minúsculas (el char de BD llega con padding)
            Assert.IsTrue(ServicioValidacionNif.TieneFormatoNif(" 12345678z "));
        }

        [TestMethod]
        public void ConstruirCondicionClientesSimplificadas_ExcluyeTodosLosClientesDeLaLista()
        {
            // NestoAPI#391: el listado de NIF incorrectos no debe mostrar los clientes de
            // facturas simplificadas (caso 31794: validación INCORRECTO antigua sin nada que
            // corregir, porque sus facturas van como F2 sin destinatario).
            var parametros = new List<object>();

            string condicion = ServicioValidacionNif.ConstruirCondicionClientesSimplificadas(parametros);

            int esperados = Constantes.ClientesEspeciales.ClientesFacturaSimplificada.Count;
            Assert.IsTrue(esperados > 0, "La lista de clientes de simplificadas no puede estar vacía");
            Assert.IsTrue(condicion.Contains("NOT IN"), "La condición debe excluir con NOT IN");
            Assert.AreEqual(esperados, parametros.Count, "Un parámetro SQL por cliente de la lista");
            foreach (string cliente in Constantes.ClientesEspeciales.ClientesFacturaSimplificada)
            {
                Assert.IsTrue(parametros.Cast<System.Data.SqlClient.SqlParameter>().Any(p => (string)p.Value == cliente),
                    $"Falta el parámetro del cliente {cliente}");
            }
        }

        [TestMethod]
        public async Task MarcarIdentificacionExtranjera_TipoOPaisInvalidos_NoGuardaNada()
        {
            ConFicha(Ficha());
            ConMasFakes();

            var tipoMalo = await servicio.MarcarIdentificacionExtranjera("30676", "99", "MA", "carlos");
            var paisMalo = await servicio.MarcarIdentificacionExtranjera("30676", "03", "MARRUECOS", "carlos");

            Assert.IsFalse(tipoMalo.Corregido);
            Assert.IsFalse(paisMalo.Corregido);
            A.CallTo(() => almacen.Guardar(A<ValidacionNifRegistro>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ObtenerEstado_MarcaExtranjeraVigente_DevuelveExtranjeroConTipoYPais()
        {
            // Vigente = la ficha no ha cambiado de NIF/nombre desde que se marcó. Ni valida
            // contra el censo, ni sale en la lista de incorrectos, ni bloquea al facturar.
            ConFicha(Ficha(nif: "AB123456", nombre: "PIERRE DUPONT"));
            A.CallTo(() => almacen.Leer("1", "30676", "0")).Returns(new ValidacionNifRegistro
            {
                Nif = "AB123456",
                Nombre = "PIERRE DUPONT",
                Estado = ServicioValidacionNif.ESTADO_EXTRANJERO,
                TipoIdentificacion = "03",
                Pais = "FR"
            });

            var resultado = await servicio.ObtenerEstado("1", "30676", "0");

            Assert.AreEqual(EstadoValidacionNif.Extranjero, resultado.Estado);
            Assert.AreEqual("03", resultado.TipoIdentificacion);
            Assert.AreEqual("FR", resultado.Pais);
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

        // NestoAPI#354: sugerencia de país para NIF-IVA intracomunitarios en la lista de NIF
        // incorrectos (la pantalla de Nesto#417 ofrece "marcar como extranjero tipo 02" con un clic).

        [TestMethod]
        public void DetectarPaisNifIvaIntracomunitario_PrefijoDePaisUeConDigitos_DevuelveElPais()
        {
            Assert.AreEqual("IT", ServicioValidacionNif.DetectarPaisNifIvaIntracomunitario("IT0280027"));   // caso NV2612580
            Assert.AreEqual("IE", ServicioValidacionNif.DetectarPaisNifIvaIntracomunitario("IE6388047T"));  // Google Ireland
            Assert.AreEqual("DE", ServicioValidacionNif.DetectarPaisNifIvaIntracomunitario(" de129273398 "));
            Assert.AreEqual("GR", ServicioValidacionNif.DetectarPaisNifIvaIntracomunitario("EL123456789"), "Grecia usa EL en el VAT pero GR como país");
            Assert.AreEqual("GB", ServicioValidacionNif.DetectarPaisNifIvaIntracomunitario("XI110305878"), "Irlanda del Norte declara con GB");
        }

        [TestMethod]
        public void DetectarPaisNifIvaIntracomunitario_NieCifEspanolYBasura_DevuelveNull()
        {
            Assert.IsNull(ServicioValidacionNif.DetectarPaisNifIvaIntracomunitario("X9495760A"), "NIE: una letra + dígitos");
            Assert.IsNull(ServicioValidacionNif.DetectarPaisNifIvaIntracomunitario("A83671234"), "CIF español");
            Assert.IsNull(ServicioValidacionNif.DetectarPaisNifIvaIntracomunitario("05231909J"), "DNI");
            Assert.IsNull(ServicioValidacionNif.DetectarPaisNifIvaIntracomunitario("ES12345678"), "ES no es extranjero");
            Assert.IsNull(ServicioValidacionNif.DetectarPaisNifIvaIntracomunitario("222222"));
            Assert.IsNull(ServicioValidacionNif.DetectarPaisNifIvaIntracomunitario("ITALIA"), "Letras sin dígitos no es un VAT");
            Assert.IsNull(ServicioValidacionNif.DetectarPaisNifIvaIntracomunitario("IT1"), "Demasiado corto");
            Assert.IsNull(ServicioValidacionNif.DetectarPaisNifIvaIntracomunitario(null));
            Assert.IsNull(ServicioValidacionNif.DetectarPaisNifIvaIntracomunitario("  "));
        }
    }
}
