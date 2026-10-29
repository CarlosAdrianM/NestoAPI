using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Eventos;
using Newtonsoft.Json;

namespace NestoAPI.Tests.Infrastructure.Eventos
{
    /// <summary>
    /// 29/10/26 (Carlos): puso un producto requerido en el evento 4 «Formación por compra Lifting Coreano» desde Nesto y no se
    /// guardó (EventosProductosRequeridos vacía), así que TNV dejaba reservar sin haberlo comprado. Causa: la lista del DTO nace
    /// creada y Json.NET, por defecto (ObjectCreationHandling.Auto), RELLENA la lista existente sin llamar al setter, que es lo
    /// que apunta el campo como recibido: TraeProductosRequeridos() daba false y la API la descartaba. Los tests de ServicioEventos
    /// asignaban la propiedad en C# y no lo veían: aquí se deserializa como lo hace el JsonMediaTypeFormatter de la API.
    /// </summary>
    [TestClass]
    public class EventoDTOJsonTests
    {
        private static EventoDTO Leer(string json) => JsonConvert.DeserializeObject<EventoDTO>(json, new JsonSerializerSettings
        {
            // Los mismos ajustes que WebApiConfig
            ReferenceLoopHandling = ReferenceLoopHandling.Serialize,
            PreserveReferencesHandling = PreserveReferencesHandling.Objects
        });

        [TestMethod]
        public void ProductosRequeridos_QueMandaNesto_SeRecibenYSeGuardan()
        {
            EventoDTO dto = Leer("{\"Titulo\":\"Lifting\",\"ProductosRequeridos\":[{\"Producto\":\"45363\"}],\"ProductosRequeridosModo\":\"Alguno\"}");

            Assert.IsTrue(dto.TraeProductosRequeridos());
            Assert.AreEqual(1, dto.ProductosRequeridos.Count);
            Assert.AreEqual("45363", dto.ProductosRequeridos[0].Producto);
        }

        [TestMethod]
        public void ProductosRequeridos_ListaVacia_SeRecibe_ParaQuitarElRequisito()
        {
            EventoDTO dto = Leer("{\"Titulo\":\"Lifting\",\"ProductosRequeridos\":[]}");

            Assert.IsTrue(dto.TraeProductosRequeridos());
            Assert.AreEqual(0, dto.ProductosRequeridos.Count);
        }

        [TestMethod]
        public void ProductosRequeridos_SinElCampo_NoSeToca()
        {
            EventoDTO dto = Leer("{\"Titulo\":\"Lifting\"}");

            Assert.IsFalse(dto.TraeProductosRequeridos());
        }

        [TestMethod]
        public void ProductosRequeridos_ANull_NoSeToca()
        {
            EventoDTO dto = Leer("{\"Titulo\":\"Lifting\",\"ProductosRequeridos\":null}");

            Assert.IsFalse(dto.TraeProductosRequeridos());
        }
    }
}
