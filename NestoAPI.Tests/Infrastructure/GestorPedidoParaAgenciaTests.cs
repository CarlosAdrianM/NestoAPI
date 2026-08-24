using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Nesto#340 (slice A3): PARIDAD DE CAMPOS. Estos tests son el contrato de la migracion del
    /// modulo de Agencias: fijan que el DTO lleva exactamente lo que Agencias leia de la entidad
    /// EF CabPedidoVta (con sus Includes) y, sobre todo, que lo lleva SIN RECORTAR.
    ///
    /// El padding es el riesgo numero uno del slice: Agencias compara Empresa, N_Cliente y
    /// Contacto SIN Trim contra listas que siguen viniendo de EF con el padding de la BD.
    /// </summary>
    [TestClass]
    public class GestorPedidoParaAgenciaTests
    {
        private const string EMPRESA_ESPEJO = "3";

        private static NVEntities CrearDb(
            IEnumerable<CabPedidoVta> pedidos = null,
            IEnumerable<Cliente> clientes = null,
            IEnumerable<PersonaContactoCliente> personas = null,
            IEnumerable<LinPedidoVta> lineas = null)
        {
            NVEntities db = A.Fake<NVEntities>();
            A.CallTo(() => db.CabPedidoVtas).Returns(CrearDbSet(pedidos ?? new List<CabPedidoVta>()));
            A.CallTo(() => db.Clientes).Returns(CrearDbSet(clientes ?? new List<Cliente>()));
            A.CallTo(() => db.PersonasContactoClientes).Returns(CrearDbSet(personas ?? new List<PersonaContactoCliente>()));
            A.CallTo(() => db.LinPedidoVtas).Returns(CrearDbSet(lineas ?? new List<LinPedidoVta>()));
            return db;
        }

        /// <summary>
        /// El fake del DbSet tiene que declarar IQueryable e IDbAsyncEnumerable: el tipo base los
        /// implementa de forma explicita y, si no se declaran aqui, FakeItEasy no puede
        /// interceptarlos ("The base type implements this interface method explicitly").
        /// </summary>
        private static DbSet<T> CrearDbSet<T>(IEnumerable<T> datos) where T : class
        {
            DbSet<T> fake = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            ConfigurarFakeDbSet(fake, datos.AsQueryable());
            return fake;
        }

        private static CabPedidoVta PedidoCompleto() => new CabPedidoVta
        {
            // Con el padding tal cual sale de la BD (char)
            Empresa = "1  ",
            Número = 924645,
            Nº_Cliente = "22709     ",
            Contacto = "0  ",
            Fecha = new DateTime(2026, 8, 21),
            Vendedor = "NV",
            Comentarios = "Llamar antes",
            ComentarioPicking = "Siempre por GLS"
        };

        private static Cliente FichaCompleta() => new Cliente
        {
            Empresa = "1  ",
            Nº_Cliente = "22709     ",
            Contacto = "0  ",
            Nombre = "SARA VILLEGAS SERRANO",
            Dirección = "CALLE DE LA REINA, 5",
            CodPostal = "28110",
            Población = "ALGETE",
            Provincia = "MADRID",
            Teléfono = "916280000"
        };

        [TestMethod]
        public void LeerPorEmpresaYNumero_PedidoConFicha_DevuelveTodosLosCamposQueUsaAgencias()
        {
            NVEntities db = CrearDb(new[] { PedidoCompleto() }, new[] { FichaCompleta() });
            var gestor = new GestorPedidoParaAgencia(db);

            PedidoParaAgenciaDTO dto = gestor.LeerPorEmpresaYNumero("1  ", 924645).Result;

            Assert.IsNotNull(dto);
            Assert.AreEqual(924645, dto.Numero);
            Assert.AreEqual(new DateTime(2026, 8, 21), dto.Fecha);
            Assert.AreEqual("NV", dto.Vendedor);
            Assert.AreEqual("Llamar antes", dto.Comentarios);
            Assert.AreEqual("Siempre por GLS", dto.ComentarioPicking);
            Assert.IsNotNull(dto.ClienteFicha);
            Assert.AreEqual("SARA VILLEGAS SERRANO", dto.ClienteFicha.Nombre);
            Assert.AreEqual("CALLE DE LA REINA, 5", dto.ClienteFicha.Direccion);
            Assert.AreEqual("28110", dto.ClienteFicha.CodPostal);
            Assert.AreEqual("ALGETE", dto.ClienteFicha.Poblacion);
            Assert.AreEqual("MADRID", dto.ClienteFicha.Provincia);
            Assert.AreEqual("916280000", dto.ClienteFicha.Telefono);
        }

        // EL RIESGO Nº 1 DEL SLICE. Agencias hace listaEmpresas.Single(e => e.Numero == pedido.Empresa)
        // contra una lista que sigue viniendo de EF con padding: si aqui recortamos, ese Single
        // lanza InvalidOperationException y se rompe la seleccion de empresa y de agencia.
        [TestMethod]
        public void LeerPorEmpresaYNumero_CamposChar_ConservanElPaddingDeLaBd()
        {
            NVEntities db = CrearDb(new[] { PedidoCompleto() }, new[] { FichaCompleta() });
            var gestor = new GestorPedidoParaAgencia(db);

            PedidoParaAgenciaDTO dto = gestor.LeerPorEmpresaYNumero("1  ", 924645).Result;

            Assert.AreEqual("1  ", dto.Empresa, "Sin el padding se rompen los Single de empresa y agencia");
            Assert.AreEqual("22709     ", dto.Cliente, "Va sin Trim a EnviosAgencia.Cliente y a busquedas por igualdad exacta");
            Assert.AreEqual("0  ", dto.Contacto, "Idem para el contacto");
        }

        // Agencias usa "pedido sin ficha" como senal para revertir al pedido anterior: no se
        // puede devolver un objeto vacio en su lugar.
        [TestMethod]
        public void LeerPorEmpresaYNumero_SinFichaDeCliente_DevuelveLaFichaNula()
        {
            NVEntities db = CrearDb(new[] { PedidoCompleto() });
            var gestor = new GestorPedidoParaAgencia(db);

            PedidoParaAgenciaDTO dto = gestor.LeerPorEmpresaYNumero("1  ", 924645).Result;

            Assert.IsNotNull(dto, "El pedido si existe");
            Assert.IsNull(dto.ClienteFicha, "Sin ficha = senal para Agencias, no un objeto vacio");
        }

        // .Any() y .ToList() se hacen sin comprobar null (con EF era un HashSet vacio).
        [TestMethod]
        public void LeerPorEmpresaYNumero_ClienteSinPersonasDeContacto_DevuelveListaVaciaNoNula()
        {
            NVEntities db = CrearDb(new[] { PedidoCompleto() }, new[] { FichaCompleta() });
            var gestor = new GestorPedidoParaAgencia(db);

            PedidoParaAgenciaDTO dto = gestor.LeerPorEmpresaYNumero("1  ", 924645).Result;

            Assert.IsNotNull(dto.ClienteFicha.PersonasContacto);
            Assert.AreEqual(0, dto.ClienteFicha.PersonasContacto.Count);
        }

        // Se devuelven TODAS, sin filtrar por cargo: el criterio de eleccion del correo vive en
        // el cliente (CorreoCliente.CorreoAgencia) y ahi se queda.
        [TestMethod]
        public void LeerPorEmpresaYNumero_ConPersonasDeContacto_DevuelveTodasConCargoYCorreo()
        {
            var personas = new[]
            {
                new PersonaContactoCliente { Empresa = "1  ", NºCliente = "22709     ", Contacto = "0  ", Cargo = 1, CorreoElectrónico = "gerente@ejemplo.es" },
                new PersonaContactoCliente { Empresa = "1  ", NºCliente = "22709     ", Contacto = "0  ", Cargo = 26, CorreoElectrónico = "agencia@ejemplo.es" }
            };
            NVEntities db = CrearDb(new[] { PedidoCompleto() }, new[] { FichaCompleta() }, personas);
            var gestor = new GestorPedidoParaAgencia(db);

            PedidoParaAgenciaDTO dto = gestor.LeerPorEmpresaYNumero("1  ", 924645).Result;

            Assert.AreEqual(2, dto.ClienteFicha.PersonasContacto.Count, "Sin filtrar por cargo");
            Assert.IsTrue(dto.ClienteFicha.PersonasContacto.Any(p => p.Cargo == 26 && p.CorreoElectronico == "agencia@ejemplo.es"));
        }

        [TestMethod]
        public void LeerPorEmpresaYNumero_PedidoQueNoExiste_DevuelveNulo()
        {
            NVEntities db = CrearDb();
            var gestor = new GestorPedidoParaAgencia(db);

            Assert.IsNull(gestor.LeerPorEmpresaYNumero("1  ", 999999).Result);
        }

        // Sin incluirEspejo se excluye la empresa espejo, que es como lo llama Agencias primero.
        [TestMethod]
        public void LeerPorNumero_SinEspejo_NoDevuelveElPedidoDeLaEmpresaEspejo()
        {
            var soloEspejo = PedidoCompleto();
            soloEspejo.Empresa = EMPRESA_ESPEJO;
            NVEntities db = CrearDb(new[] { soloEspejo });
            var gestor = new GestorPedidoParaAgencia(db);

            Assert.IsNull(gestor.LeerPorNumero(924645, incluirEspejo: false).Result,
                "Agencias busca primero fuera de la empresa espejo");
        }

        // Y el fallback (la sobrecarga de un argumento) si lo acepta.
        [TestMethod]
        public void LeerPorNumero_ConEspejo_SiDevuelveElPedidoDeLaEmpresaEspejo()
        {
            var soloEspejo = PedidoCompleto();
            soloEspejo.Empresa = EMPRESA_ESPEJO;
            NVEntities db = CrearDb(new[] { soloEspejo });
            var gestor = new GestorPedidoParaAgencia(db);

            PedidoParaAgenciaDTO dto = gestor.LeerPorNumero(924645, incluirEspejo: true).Result;

            Assert.IsNotNull(dto, "Si el numero solo existe en la espejo, el fallback lo acepta");
            Assert.AreEqual(EMPRESA_ESPEJO, dto.Empresa);
        }

        [TestMethod]
        public void LeerPorFactura_ConLinea_DevuelveElPedidoDeEsaFactura()
        {
            var lineas = new[] { new LinPedidoVta { Empresa = "1  ", Número = 924645, Nº_Factura = "NV2612345" } };
            NVEntities db = CrearDb(new[] { PedidoCompleto() }, new[] { FichaCompleta() }, null, lineas);
            var gestor = new GestorPedidoParaAgencia(db);

            PedidoParaAgenciaDTO dto = gestor.LeerPorFactura("NV2612345").Result;

            Assert.IsNotNull(dto);
            Assert.AreEqual(924645, dto.Numero);
        }

        // El original se quedaba con pedido = 0 (default de Integer) y buscaba el pedido numero 0.
        [TestMethod]
        public void LeerPorFactura_SinNingunaLinea_DevuelveNuloYNoBuscaElPedidoCero()
        {
            var pedidoCero = PedidoCompleto();
            pedidoCero.Número = 0;
            NVEntities db = CrearDb(new[] { pedidoCero });
            var gestor = new GestorPedidoParaAgencia(db);

            Assert.IsNull(gestor.LeerPorFactura("NO_EXISTE").Result,
                "Sin linea con esa factura no hay pedido, aunque exista un pedido numero 0");
        }

        // ===== Cuarto modo: buscar por texto de cliente =====
        // Sustituye al fallback de CalcularPedidoTexto, que en Nesto eran dos pasos
        // (CargarClientePorUnDato + navegar cliente.CabPedidoVta). El segundo paso navegaba una
        // propiedad con lazy loading sobre un DbContext ya cerrado por su Using, asi que
        // reventaba con ObjectDisposedException en cuanto la busqueda encontraba cliente. Aqui
        // es una sola consulta.

        [TestMethod]
        public void LeerPorTextoDeCliente_PorNombre_DevuelveElPedidoMasRecienteDeEseCliente()
        {
            var antiguo = PedidoCompleto();
            antiguo.Número = 900000;
            var reciente = PedidoCompleto();
            reciente.Número = 924645;
            NVEntities db = CrearDb(new[] { antiguo, reciente }, new[] { FichaCompleta() });
            var gestor = new GestorPedidoParaAgencia(db);

            PedidoParaAgenciaDTO dto = gestor.LeerPorTextoDeCliente("1  ", "VILLEGAS").Result;

            Assert.IsNotNull(dto);
            Assert.AreEqual(924645, dto.Numero, "Se coge el pedido de numero mas alto, como el OrderByDescending original");
        }

        [TestMethod]
        public void LeerPorTextoDeCliente_PorDireccionOPorTelefono_TambienEncuentra()
        {
            NVEntities db = CrearDb(new[] { PedidoCompleto() }, new[] { FichaCompleta() });
            var gestor = new GestorPedidoParaAgencia(db);

            Assert.IsNotNull(gestor.LeerPorTextoDeCliente("1  ", "REINA").Result, "por direccion");
            Assert.IsNotNull(gestor.LeerPorTextoDeCliente("1  ", "916280000").Result, "por telefono");
        }

        [TestMethod]
        public void LeerPorTextoDeCliente_DeOtraEmpresa_NoLoEncuentra()
        {
            NVEntities db = CrearDb(new[] { PedidoCompleto() }, new[] { FichaCompleta() });
            var gestor = new GestorPedidoParaAgencia(db);

            Assert.IsNull(gestor.LeerPorTextoDeCliente("2  ", "VILLEGAS").Result);
        }

        [TestMethod]
        public void LeerPorTextoDeCliente_ClienteSinPedidos_DevuelveNulo()
        {
            NVEntities db = CrearDb(clientes: new[] { FichaCompleta() });
            var gestor = new GestorPedidoParaAgencia(db);

            Assert.IsNull(gestor.LeerPorTextoDeCliente("1  ", "VILLEGAS").Result,
                "Hay cliente pero no tiene pedidos: Agencias lo trata como 'no encontrado'");
        }

        [TestMethod]
        public void LeerPorTextoDeCliente_TextoVacio_DevuelveNuloSinConsultar()
        {
            NVEntities db = CrearDb(new[] { PedidoCompleto() }, new[] { FichaCompleta() });
            var gestor = new GestorPedidoParaAgencia(db);

            Assert.IsNull(gestor.LeerPorTextoDeCliente("1  ", null).Result);
            Assert.IsNull(gestor.LeerPorTextoDeCliente("1  ", "   ").Result);
        }

        // Mismo contrato de padding que los otros tres modos.
        [TestMethod]
        public void LeerPorTextoDeCliente_CamposChar_ConservanElPaddingDeLaBd()
        {
            NVEntities db = CrearDb(new[] { PedidoCompleto() }, new[] { FichaCompleta() });
            var gestor = new GestorPedidoParaAgencia(db);

            PedidoParaAgenciaDTO dto = gestor.LeerPorTextoDeCliente("1  ", "VILLEGAS").Result;

            Assert.AreEqual("1  ", dto.Empresa);
            Assert.AreEqual("22709     ", dto.Cliente);
            Assert.AreEqual("0  ", dto.Contacto);
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
    }
}
