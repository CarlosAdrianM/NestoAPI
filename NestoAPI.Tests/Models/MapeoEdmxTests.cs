using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace NestoAPI.Tests.Models
{
    /// <summary>
    /// Comprueba que las tres capas del EDMX (CSDL, SSDL y MSL) casan entre sí y con las clases
    /// generadas. Hace falta porque el EDMX se edita a veces a mano y el refresh de Visual Studio
    /// ha dejado propiedades fantasma más de una vez (#413): un mapeo roto NO da error de
    /// compilación, revienta en runtime la primera vez que alguien usa esa entidad.
    ///
    /// Se lee el XML de los recursos incrustados en vez de levantar EF porque el proyecto de tests
    /// no tiene proveedor de SQL Server registrado (ni falta que hace para esta comprobación).
    /// </summary>
    [TestClass]
    public class MapeoEdmxTests
    {
        private static XNamespace CsdlNs => "http://schemas.microsoft.com/ado/2009/11/edm";
        private static XNamespace SsdlNs => "http://schemas.microsoft.com/ado/2009/11/edm/ssdl";
        private static XNamespace MslNs => "http://schemas.microsoft.com/ado/2009/11/mapping/cs";

        private static XDocument LeerRecurso(string extension)
        {
            Assembly ensamblado = typeof(NVEntities).Assembly;
            string nombre = ensamblado.GetManifestResourceNames()
                .Single(r => r.EndsWith($"NestoEntities.{extension}", StringComparison.OrdinalIgnoreCase));

            using (Stream flujo = ensamblado.GetManifestResourceStream(nombre))
            {
                return XDocument.Load(flujo);
            }
        }

        private static Dictionary<string, XElement> EntidadesPorNombre(XDocument documento, XNamespace ns)
        {
            return documento.Descendants(ns + "EntityType")
                .ToDictionary(e => e.Attribute("Name").Value, e => e);
        }

        [TestMethod]
        public void Edmx_CadaPropiedadDelModeloEstaMapeadaAUnaColumnaQueExiste()
        {
            Dictionary<string, XElement> conceptual = EntidadesPorNombre(LeerRecurso("csdl"), CsdlNs);
            Dictionary<string, XElement> almacen = EntidadesPorNombre(LeerRecurso("ssdl"), SsdlNs);
            XDocument msl = LeerRecurso("msl");

            List<string> fallos = new List<string>();

            foreach (XElement mapeoConjunto in msl.Descendants(MslNs + "EntitySetMapping"))
            {
                foreach (XElement mapeoTipo in mapeoConjunto.Elements(MslNs + "EntityTypeMapping"))
                {
                    // TypeName viene como "NVModel.NotificacionBuzon" (o IsTypeOf(...) en jerarquías)
                    string nombreEntidad = mapeoTipo.Attribute("TypeName").Value
                        .Replace("IsTypeOf(", "").Replace(")", "");
                    nombreEntidad = nombreEntidad.Substring(nombreEntidad.LastIndexOf('.') + 1);

                    if (!conceptual.TryGetValue(nombreEntidad, out XElement entidadConceptual))
                    {
                        fallos.Add($"El MSL mapea '{nombreEntidad}', que no existe en el CSDL");
                        continue;
                    }

                    foreach (XElement fragmento in mapeoTipo.Elements(MslNs + "MappingFragment"))
                    {
                        string tablaAlmacen = fragmento.Attribute("StoreEntitySet").Value;

                        if (!almacen.TryGetValue(tablaAlmacen, out XElement entidadAlmacen))
                        {
                            fallos.Add($"'{nombreEntidad}' se mapea a la tabla '{tablaAlmacen}', que no existe en el SSDL");
                            continue;
                        }

                        HashSet<string> columnas = new HashSet<string>(
                            entidadAlmacen.Elements(SsdlNs + "Property").Select(p => p.Attribute("Name").Value));
                        HashSet<string> propiedades = new HashSet<string>(
                            entidadConceptual.Elements(CsdlNs + "Property").Select(p => p.Attribute("Name").Value));

                        foreach (XElement escalar in fragmento.Elements(MslNs + "ScalarProperty"))
                        {
                            string propiedad = escalar.Attribute("Name").Value;
                            string columna = escalar.Attribute("ColumnName").Value;

                            if (!propiedades.Contains(propiedad))
                            {
                                fallos.Add($"{nombreEntidad}.{propiedad} está en el MSL pero no en el CSDL");
                            }

                            if (!columnas.Contains(columna))
                            {
                                fallos.Add($"{nombreEntidad}.{propiedad} apunta a la columna '{columna}', que no existe en '{tablaAlmacen}'");
                            }
                        }
                    }
                }
            }

            Assert.AreEqual(0, fallos.Count, string.Join(Environment.NewLine, fallos));
        }

        /// <summary>
        /// NestoAPI#482 (slice 1): CabPedidoVta.ModoServicio se añadió a mano en las tres capas. Es
        /// NULLABLE a propósito (NULL = manda ServirJunto; ALTER instantáneo, sin backfill): si un
        /// refresh del modelo lo dejara NOT NULL, EF mandaría 0 en cada insert y saltaría el CHECK.
        /// </summary>
        [TestMethod]
        public void Edmx_ModoServicioDelPedido_EstaEnLasTresCapasYEsNullable()
        {
            XElement almacen = EntidadesPorNombre(LeerRecurso("ssdl"), SsdlNs)["CabPedidoVta"]
                .Elements(SsdlNs + "Property").Single(p => p.Attribute("Name").Value == "ModoServicio");
            XElement conceptual = EntidadesPorNombre(LeerRecurso("csdl"), CsdlNs)["CabPedidoVta"]
                .Elements(CsdlNs + "Property").Single(p => p.Attribute("Name").Value == "ModoServicio");
            bool mapeada = LeerRecurso("msl").Descendants(MslNs + "ScalarProperty")
                .Any(p => p.Attribute("Name").Value == "ModoServicio" && p.Attribute("ColumnName").Value == "ModoServicio");

            Assert.AreEqual("tinyint", (string)almacen.Attribute("Type"));
            Assert.AreEqual("true", (string)almacen.Attribute("Nullable"));
            Assert.AreEqual("Byte", (string)conceptual.Attribute("Type"));
            Assert.AreNotEqual("false", (string)conceptual.Attribute("Nullable"));
            Assert.IsTrue(mapeada);
            Assert.AreEqual(typeof(byte?), typeof(CabPedidoVta).GetProperty("ModoServicio").PropertyType);
        }

        /// <summary>
        /// NestoAPI#481 (y #456 antes): con Usuario marcado Computed, EF no manda la columna y el
        /// DEFAULT suser_sname() graba la cuenta del pool (NUEVAVISION\RDS2016$) aunque el código
        /// asigne el usuario. Las entidades en las que graba una PERSONA no pueden llevarlo.
        /// </summary>
        [TestMethod]
        public void Edmx_ElUsuarioDeAuditoriaNoEsComputedEnLasEntidadesQueGrabanPersonas()
        {
            // #481 punto 3 (15/09/26): Inventarios (PostInventario resuelve el usuario del Identity),
            // VendedoresClienteGrupoProducto y VendedoresPedidoGrupoProducto (GestorClientes y
            // GestorComisiones asignan Usuario en todos los inserts). Se quedan Computed a propósito:
            // Ubicaciones (el picking las inserta sin usuario: habría que resolverlo antes),
            // InventariosCuadre (la API no inserta), y las de jobs (Comisiones*, Modificaciones).
            string[] entidadesDePersonas = { "OfertasPermitidas", "Inventarios", "VendedoresClienteGrupoProducto", "VendedoresPedidoGrupoProducto" };
            Dictionary<string, XElement> almacen = EntidadesPorNombre(LeerRecurso("ssdl"), SsdlNs);

            foreach (string entidad in entidadesDePersonas)
            {
                XElement usuario = almacen[entidad].Elements(SsdlNs + "Property")
                    .Single(p => p.Attribute("Name").Value == "Usuario");
                Assert.AreNotEqual("Computed", (string)usuario.Attribute("StoreGeneratedPattern"),
                    $"{entidad}.Usuario es Computed: lo que asigne el código se pierde y graba el pool");
            }
        }

        /// <summary>
        /// 10/09/26: la API se publicó con una multiplicidad inválida en FK_ProductosVariantes_Productos
        /// (error 0113) y TODAS las peticiones con base de datos cayeron. Los tests con NVEntities falso
        /// no cargan los metadatos; este los carga exactamente como EF al arrancar (sin base de datos).
        /// </summary>
        [TestMethod]
        public void Edmx_LosMetadatosCarganComoLoHaceEntityFrameworkAlArrancar()
        {
            // Solo el modelo conceptual: el de almacén exige el proveedor SqlClient registrado en el
            // app.config de los tests y no aporta reglas nuevas (las multiplicidades se validan aquí).
            using (var csdl = LeerRecurso("csdl").CreateReader())
            {
                var conceptual = new System.Data.Entity.Core.Metadata.Edm.EdmItemCollection(new[] { csdl });
                Assert.IsTrue(conceptual.GetItems<System.Data.Entity.Core.Metadata.Edm.EntityType>().Any());
            }
        }

        [TestMethod]
        public void Edmx_NingunaEntidadTienePropiedadesFantasma()
        {
            // Una propiedad en el CSDL que no exista en la clase generada es justo lo que dejó el
            // refresh del EDMX en #413, y EF revienta al materializar.
            Assembly ensamblado = typeof(NVEntities).Assembly;
            Dictionary<string, XElement> conceptual = EntidadesPorNombre(LeerRecurso("csdl"), CsdlNs);

            List<string> fantasmas = new List<string>();

            foreach (KeyValuePair<string, XElement> entidad in conceptual)
            {
                Type tipo = ensamblado.GetType($"NestoAPI.Models.{entidad.Key}");

                if (tipo == null)
                {
                    continue; // sin clase POCO con ese nombre: no es cosa de este test
                }

                foreach (XElement propiedad in entidad.Value.Elements(CsdlNs + "Property"))
                {
                    string nombre = propiedad.Attribute("Name").Value;

                    if (tipo.GetProperty(nombre) == null)
                    {
                        fantasmas.Add($"{entidad.Key}.{nombre} está en el EDMX pero no en la clase");
                    }
                }
            }

            Assert.AreEqual(0, fantasmas.Count, string.Join(Environment.NewLine, fantasmas));
        }

        /// <summary>
        /// NestoAPI#522: CabFacturaVta.VerifactuIncidencia («pendiente por incidencia» técnica de
        /// Verifactu) se añadió a mano en las tres capas. NULLABLE a propósito: ALTER instantáneo, sin
        /// backfill, y prdCrearFacturaVta / Nesto viejo no la conocen (NULL = sin incidencia).
        /// </summary>
        [TestMethod]
        public void Edmx_VerifactuIncidenciaDeLaFactura_EstaEnLasTresCapasYEsNullable()
        {
            XElement almacen = EntidadesPorNombre(LeerRecurso("ssdl"), SsdlNs)["CabFacturaVta"]
                .Elements(SsdlNs + "Property").Single(p => p.Attribute("Name").Value == "VerifactuIncidencia");
            XElement conceptual = EntidadesPorNombre(LeerRecurso("csdl"), CsdlNs)["CabFacturaVta"]
                .Elements(CsdlNs + "Property").Single(p => p.Attribute("Name").Value == "VerifactuIncidencia");
            bool mapeada = LeerRecurso("msl").Descendants(MslNs + "ScalarProperty")
                .Any(p => p.Attribute("Name").Value == "VerifactuIncidencia" && p.Attribute("ColumnName").Value == "VerifactuIncidencia");

            Assert.AreEqual("bit", (string)almacen.Attribute("Type"));
            Assert.AreNotEqual("false", (string)almacen.Attribute("Nullable"));
            Assert.AreEqual("Boolean", (string)conceptual.Attribute("Type"));
            Assert.AreNotEqual("false", (string)conceptual.Attribute("Nullable"));
            Assert.IsTrue(mapeada);
            Assert.AreEqual(typeof(bool?), typeof(CabFacturaVta).GetProperty("VerifactuIncidencia").PropertyType);
        }

        /// <summary>
        /// NestoAPI#522 (parte 1): CabFacturaVta.VerifactuEnviadaProvisional (el cliente recibió por correo el
        /// justificante provisional y hay que mandarle la definitiva) se añadió a mano en las tres capas.
        /// NULLABLE por lo mismo que VerifactuIncidencia: ALTER instantáneo y los escritores viejos no la conocen.
        /// </summary>
        [TestMethod]
        public void Edmx_VerifactuEnviadaProvisionalDeLaFactura_EstaEnLasTresCapasYEsNullable()
        {
            XElement almacen = EntidadesPorNombre(LeerRecurso("ssdl"), SsdlNs)["CabFacturaVta"]
                .Elements(SsdlNs + "Property").Single(p => p.Attribute("Name").Value == "VerifactuEnviadaProvisional");
            XElement conceptual = EntidadesPorNombre(LeerRecurso("csdl"), CsdlNs)["CabFacturaVta"]
                .Elements(CsdlNs + "Property").Single(p => p.Attribute("Name").Value == "VerifactuEnviadaProvisional");
            bool mapeada = LeerRecurso("msl").Descendants(MslNs + "ScalarProperty")
                .Any(p => p.Attribute("Name").Value == "VerifactuEnviadaProvisional" && p.Attribute("ColumnName").Value == "VerifactuEnviadaProvisional");

            Assert.AreEqual("bit", (string)almacen.Attribute("Type"));
            Assert.AreNotEqual("false", (string)almacen.Attribute("Nullable"));
            Assert.AreEqual("Boolean", (string)conceptual.Attribute("Type"));
            Assert.AreNotEqual("false", (string)conceptual.Attribute("Nullable"));
            Assert.IsTrue(mapeada);
            Assert.AreEqual(typeof(bool?), typeof(CabFacturaVta).GetProperty("VerifactuEnviadaProvisional").PropertyType);
        }

        /// <summary>
        /// NestoAPI#392: CabFacturaVta.VerifactuDeclararSimplificada (factura completa con NIF inconseguible que se
        /// declara como F2, y su rectificativa como R5) se añadió a mano en las tres capas. NULLABLE: ALTER
        /// instantáneo y los escritores viejos no la conocen.
        /// </summary>
        [TestMethod]
        public void Edmx_VerifactuDeclararSimplificadaDeLaFactura_EstaEnLasTresCapasYEsNullable()
        {
            XElement almacen = EntidadesPorNombre(LeerRecurso("ssdl"), SsdlNs)["CabFacturaVta"]
                .Elements(SsdlNs + "Property").Single(p => p.Attribute("Name").Value == "VerifactuDeclararSimplificada");
            XElement conceptual = EntidadesPorNombre(LeerRecurso("csdl"), CsdlNs)["CabFacturaVta"]
                .Elements(CsdlNs + "Property").Single(p => p.Attribute("Name").Value == "VerifactuDeclararSimplificada");
            bool mapeada = LeerRecurso("msl").Descendants(MslNs + "ScalarProperty")
                .Any(p => p.Attribute("Name").Value == "VerifactuDeclararSimplificada" && p.Attribute("ColumnName").Value == "VerifactuDeclararSimplificada");

            Assert.AreEqual("bit", (string)almacen.Attribute("Type"));
            Assert.AreNotEqual("false", (string)almacen.Attribute("Nullable"));
            Assert.AreEqual("Boolean", (string)conceptual.Attribute("Type"));
            Assert.AreNotEqual("false", (string)conceptual.Attribute("Nullable"));
            Assert.IsTrue(mapeada);
            Assert.AreEqual(typeof(bool?), typeof(CabFacturaVta).GetProperty("VerifactuDeclararSimplificada").PropertyType);
        }

        [TestMethod]
        public void Edmx_NotificacionesBuzon_EstaCompletaEnLasTresCapas()
        {
            string[] esperadas =
            {
                "Id", "Usuario", "Empresa", "Vendedor", "Cliente", "Contacto", "Aplicacion",
                "Titulo", "Cuerpo", "Datos", "FechaCreacion", "FechaLeida", "FechaEliminada"
            };

            XElement conceptual = EntidadesPorNombre(LeerRecurso("csdl"), CsdlNs)["NotificacionBuzon"];
            XElement almacen = EntidadesPorNombre(LeerRecurso("ssdl"), SsdlNs)["NotificacionesBuzon"];
            XElement mapeo = LeerRecurso("msl").Descendants(MslNs + "EntitySetMapping")
                .Single(m => m.Attribute("Name").Value == "NotificacionesBuzon");

            CollectionAssert.AreEquivalent(esperadas,
                conceptual.Elements(CsdlNs + "Property").Select(p => p.Attribute("Name").Value).ToArray());
            CollectionAssert.AreEquivalent(esperadas,
                almacen.Elements(SsdlNs + "Property").Select(p => p.Attribute("Name").Value).ToArray());
            CollectionAssert.AreEquivalent(esperadas,
                mapeo.Descendants(MslNs + "ScalarProperty").Select(p => p.Attribute("Name").Value).ToArray());
        }

        /// <summary>NestoAPI#577: calendario de reposiciones añadido a mano; las horas son time ↔ Time ↔ TimeSpan.</summary>
        [TestMethod]
        public void Edmx_ReposicionesCalendario_EstaCompletaEnLasTresCapasConLasHorasComoTime()
        {
            string[] esperadas =
            {
                "Id", "Empresa", "AlmacenOrigen", "AlmacenDestino", "DiaSemana", "HoraCierre", "HoraLlegadaHabitual",
                "Activo", "Usuario", "FechaModificacion"
            };

            XElement conceptual = EntidadesPorNombre(LeerRecurso("csdl"), CsdlNs)["ReposicionCalendario"];
            XElement almacen = EntidadesPorNombre(LeerRecurso("ssdl"), SsdlNs)["ReposicionesCalendario"];
            XElement mapeo = LeerRecurso("msl").Descendants(MslNs + "EntitySetMapping")
                .Single(m => m.Attribute("Name").Value == "ReposicionesCalendario");

            CollectionAssert.AreEquivalent(esperadas,
                conceptual.Elements(CsdlNs + "Property").Select(p => p.Attribute("Name").Value).ToArray());
            CollectionAssert.AreEquivalent(esperadas,
                almacen.Elements(SsdlNs + "Property").Select(p => p.Attribute("Name").Value).ToArray());
            CollectionAssert.AreEquivalent(esperadas,
                mapeo.Descendants(MslNs + "ScalarProperty").Select(p => p.Attribute("Name").Value).ToArray());
            Assert.AreEqual("time", (string)almacen.Elements(SsdlNs + "Property").Single(p => p.Attribute("Name").Value == "HoraCierre").Attribute("Type"));
            Assert.AreEqual("Time", (string)conceptual.Elements(CsdlNs + "Property").Single(p => p.Attribute("Name").Value == "HoraCierre").Attribute("Type"));
            Assert.AreEqual(typeof(TimeSpan), typeof(ReposicionCalendario).GetProperty("HoraCierre").PropertyType);
            Assert.AreEqual(typeof(byte), typeof(ReposicionCalendario).GetProperty("DiaSemana").PropertyType);
        }

        /// <summary>NestoAPI#591: eventos y señales añadidos a mano; la fecha del evento es date ↔ DateTime y los importes decimal(18,2).</summary>
        [TestMethod]
        public void Edmx_EventosYEventosSenales_EstanCompletasEnLasTresCapas()
        {
            ComprobarTresCapas("Evento", "Eventos",
                "Id", "Empresa", "Titulo", "Fecha", "ImporteSenal", "Activo", "Usuario", "FechaModificacion");
            ComprobarTresCapas("EventoSenal", "EventosSenales",
                "Id", "EventoId", "Empresa", "Cliente", "Contacto", "NumOrdenExtracto", "Importe", "Usuario", "FechaModificacion");

            XElement almacenEvento = EntidadesPorNombre(LeerRecurso("ssdl"), SsdlNs)["Eventos"];
            XElement almacenSenal = EntidadesPorNombre(LeerRecurso("ssdl"), SsdlNs)["EventosSenales"];
            Assert.AreEqual("date", (string)almacenEvento.Elements(SsdlNs + "Property").Single(p => p.Attribute("Name").Value == "Fecha").Attribute("Type"));
            Assert.AreEqual("nvarchar", (string)almacenEvento.Elements(SsdlNs + "Property").Single(p => p.Attribute("Name").Value == "Titulo").Attribute("Type"));
            Assert.AreEqual("decimal", (string)almacenSenal.Elements(SsdlNs + "Property").Single(p => p.Attribute("Name").Value == "Importe").Attribute("Type"));
            Assert.AreEqual(typeof(DateTime), typeof(Evento).GetProperty("Fecha").PropertyType);
            Assert.AreEqual(typeof(decimal), typeof(EventoSenal).GetProperty("Importe").PropertyType);
            Assert.AreEqual(typeof(int), typeof(EventoSenal).GetProperty("NumOrdenExtracto").PropertyType);
        }

        /// <summary>NestoAPI#603: registro de sugerencias de contacto añadido a mano; Probabilidad es real ↔ float y RapportId/FechaAtendida admiten null.</summary>
        [TestMethod]
        public void Edmx_SugerenciasContacto_EstaCompletaEnLasTresCapas()
        {
            ComprobarTresCapas("SugerenciaContacto", "SugerenciasContacto",
                "Id", "Fecha", "Vendedor", "Usuario", "Cliente", "Contacto", "Prioridad", "Orden", "Probabilidad", "Motivo",
                "Atendida", "RapportId", "FechaAtendida");

            XElement almacen = EntidadesPorNombre(LeerRecurso("ssdl"), SsdlNs)["SugerenciasContacto"];
            Assert.AreEqual("real", (string)almacen.Elements(SsdlNs + "Property").Single(p => p.Attribute("Name").Value == "Probabilidad").Attribute("Type"));
            Assert.AreEqual("nvarchar", (string)almacen.Elements(SsdlNs + "Property").Single(p => p.Attribute("Name").Value == "Motivo").Attribute("Type"));
            Assert.AreEqual(typeof(float), typeof(SugerenciaContacto).GetProperty("Probabilidad").PropertyType);
            Assert.AreEqual(typeof(int?), typeof(SugerenciaContacto).GetProperty("RapportId").PropertyType);
            Assert.AreEqual(typeof(DateTime?), typeof(SugerenciaContacto).GetProperty("FechaAtendida").PropertyType);
        }

        private static void ComprobarTresCapas(string entidad, string conjunto, params string[] esperadas)
        {
            XElement conceptual = EntidadesPorNombre(LeerRecurso("csdl"), CsdlNs)[entidad];
            XElement almacen = EntidadesPorNombre(LeerRecurso("ssdl"), SsdlNs)[conjunto];
            XElement mapeo = LeerRecurso("msl").Descendants(MslNs + "EntitySetMapping")
                .Single(m => m.Attribute("Name").Value == conjunto);

            CollectionAssert.AreEquivalent(esperadas,
                conceptual.Elements(CsdlNs + "Property").Select(p => p.Attribute("Name").Value).ToArray());
            CollectionAssert.AreEquivalent(esperadas,
                almacen.Elements(SsdlNs + "Property").Select(p => p.Attribute("Name").Value).ToArray());
            CollectionAssert.AreEquivalent(esperadas,
                mapeo.Descendants(MslNs + "ScalarProperty").Select(p => p.Attribute("Name").Value).ToArray());
            CollectionAssert.AreEquivalent(esperadas,
                typeof(NVEntities).Assembly.GetType("NestoAPI.Models." + entidad).GetProperties().Select(p => p.Name).ToArray());
        }
    }
}
