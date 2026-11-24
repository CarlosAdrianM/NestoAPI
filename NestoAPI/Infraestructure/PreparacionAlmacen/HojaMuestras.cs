using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>NestoAPI#884 (d): una muestra en la hoja de mostrador.</summary>
    public class FichaHojaMuestra
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Familia { get; set; }
        /// <summary>El EAN-13 de la unidad (fabricante o interno del 29); null si aún no tiene.</summary>
        public string CodigoBarras { get; set; }
        /// <summary>La foto de la tienda online, si se ha podido traer.</summary>
        public byte[] Foto { get; set; }
    }

    /// <summary>NestoAPI#884 (d): lo que lleva la hoja.</summary>
    public class HojaMuestras
    {
        public string Almacen { get; set; }
        public string Familia { get; set; }
        public bool SoloConStock { get; set; }
        public DateTime Fecha { get; set; }
        public List<FichaHojaMuestra> Muestras { get; set; } = new List<FichaHojaMuestra>();
    }

    public interface IServicioHojaMuestras
    {
        /// <exception cref="ArgumentException">Almacén que no vale o «solo con stock» sin almacén: el motivo es para el usuario.</exception>
        Task<HojaMuestras> Preparar(string empresa, string almacen, string familia, bool soloConStock, bool conFotos);

        byte[] GenerarPdf(HojaMuestras hoja);
    }

    /// <summary>
    /// NestoAPI#884 (d, plan B de la hoja de mostrador): las muestras de una familia (o todas) con nombre, referencia, su EAN-13
    /// dibujado y la foto de la tienda online, agrupadas por familia, en PDF (QuestPDF, como los demás informes). Reimprimible:
    /// solo lee. La foto sale de la misma caché que las de Ariadna (<see cref="IFotosProductoAlmacen"/>) y se descarga en paralelo
    /// con un tope de tiempo: la que no llega, sin foto (la hoja sale igual).
    /// </summary>
    public class ServicioHojaMuestras : IServicioHojaMuestras
    {
        internal const int DESCARGAS_EN_PARALELO = 8;
        internal static readonly TimeSpan TIEMPO_DESCARGA = TimeSpan.FromSeconds(10);

        private static readonly HttpClient clienteFotos = new HttpClient { Timeout = TIEMPO_DESCARGA };

        private readonly IRepositorioMuestras repositorio;
        private readonly IFotosProductoAlmacen fotos;
        private readonly Func<string, Task<byte[]>> descargar;
        private readonly Func<DateTime> ahora;

        public ServicioHojaMuestras(IRepositorioMuestras repositorio, IFotosProductoAlmacen fotos)
            : this(repositorio, fotos, url => clienteFotos.GetByteArrayAsync(url), () => DateTime.Now)
        {
        }

        internal ServicioHojaMuestras(IRepositorioMuestras repositorio, IFotosProductoAlmacen fotos, Func<string, Task<byte[]>> descargar, Func<DateTime> ahora)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.fotos = fotos;
            this.descargar = descargar;
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        public async Task<HojaMuestras> Preparar(string empresa, string almacen, string familia, bool soloConStock, bool conFotos)
        {
            string emp = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            string elAlmacen = null;
            if (!string.IsNullOrWhiteSpace(almacen))
            {
                elAlmacen = ReglasMuestras.NormalizarAlmacen(almacen) ?? throw new ArgumentException(ReglasMuestras.MensajeAlmacen);
            }
            else if (soloConStock)
            {
                throw new ArgumentException("Para sacar solo las muestras con stock, diga de qué almacén (ALG, REI o ALC).");
            }

            List<MuestraAlmacen> muestras = ReglasMuestras.Filtrar(await repositorio.LeerMuestras(emp, elAlmacen).ConfigureAwait(false),
                familia, soloConStock, null);
            var hoja = new HojaMuestras
            {
                Almacen = elAlmacen,
                Familia = string.IsNullOrWhiteSpace(familia) ? null : familia.Trim(),
                SoloConStock = soloConStock,
                Fecha = ahora(),
                Muestras = Ordenar(muestras).Select(m => new FichaHojaMuestra
                {
                    Producto = m.Producto?.Trim(),
                    Nombre = m.Nombre?.Trim(),
                    Familia = ReglasMuestras.NombreFamilia(m),
                    CodigoBarras = ServicioEtiquetasProducto.CodigoDeLaUnidad(m.Codigos)?.Codigo?.Trim()
                }).ToList()
            };
            if (conFotos && hoja.Muestras.Count > 0)
            {
                await PonerFotos(hoja.Muestras).ConfigureAwait(false);
            }
            return hoja;
        }

        /// <summary>Por familia (las de sin familia al final) y nombre: el orden de la hoja.</summary>
        internal static List<MuestraAlmacen> Ordenar(IEnumerable<MuestraAlmacen> muestras)
        {
            StringComparer texto = StringComparer.Create(CultureInfo.GetCultureInfo("es-ES"), ignoreCase: true);
            return (muestras ?? Enumerable.Empty<MuestraAlmacen>())
                .OrderBy(m => string.IsNullOrWhiteSpace(ReglasMuestras.NombreFamilia(m)) ? 1 : 0)
                .ThenBy(m => ReglasMuestras.NombreFamilia(m) ?? string.Empty, texto)
                .ThenBy(m => m.Nombre?.Trim() ?? string.Empty, texto)
                .ThenBy(m => m.Producto?.Trim(), StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Nunca falla: la foto que no se consigue, no sale.</summary>
        private async Task PonerFotos(List<FichaHojaMuestra> muestras)
        {
            if (fotos == null || descargar == null)
            {
                return;
            }
            IDictionary<string, string> urls;
            try
            {
                urls = await fotos.Urls(muestras.Select(m => m.Producto)).ConfigureAwait(false) ?? new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Hoja de muestras #884] Sin fotos: no se han podido saber sus direcciones: {ex.Message}", ex));
                return;
            }
            using (var turnos = new SemaphoreSlim(DESCARGAS_EN_PARALELO))
            {
                await Task.WhenAll(muestras.Select(async m =>
                {
                    if (m.Producto == null || !urls.TryGetValue(m.Producto, out string url) || string.IsNullOrWhiteSpace(url))
                    {
                        return;
                    }
                    await turnos.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        byte[] bytes = await descargar(url).ConfigureAwait(false);
                        m.Foto = bytes != null && bytes.Length > 0 ? bytes : null;
                    }
                    catch
                    {
                        // Sin foto: la hoja sale igual
                    }
                    finally
                    {
                        _ = turnos.Release();
                    }
                })).ConfigureAwait(false);
            }
        }

        public byte[] GenerarPdf(HojaMuestras hoja) => GeneradorPdfHojaMuestras.Generar(hoja);
    }

    /// <summary>NestoAPI#884 (d): el PDF de la hoja de muestras: A4, tres por fila, agrupadas por familia.</summary>
    public static class GeneradorPdfHojaMuestras
    {
        internal const int COLUMNAS = 3;
        /// <summary>Un módulo del EAN-13 en puntos PDF (95 módulos = 104,5 pt ≈ 3,7 cm; un poco más del 100 % nominal de 0,33 mm).</summary>
        internal const float MODULO = 1.1f;
        internal const float ALTO_BARRAS = 34f;
        internal const float ALTO_FOTO = 56f;

        public static byte[] Generar(HojaMuestras hoja)
        {
            HojaMuestras datos = hoja ?? new HojaMuestras { Fecha = DateTime.Now };
            List<IGrouping<string, FichaHojaMuestra>> familias = (datos.Muestras ?? new List<FichaHojaMuestra>())
                .GroupBy(m => string.IsNullOrWhiteSpace(m.Familia) ? "Sin familia" : m.Familia.Trim())
                .ToList();
            return Document.Create(documento =>
            {
                documento.Page(pagina =>
                {
                    pagina.Size(PageSizes.A4);
                    pagina.MarginVertical(1f, Unit.Centimetre);
                    pagina.MarginHorizontal(1f, Unit.Centimetre);
                    pagina.DefaultTextStyle(x => x.FontSize(8));
                    pagina.Header().Element(c => Cabecera(c, datos));
                    pagina.Content().Column(columna =>
                    {
                        if (familias.Count == 0)
                        {
                            columna.Item().PaddingTop(20).Text("No hay muestras con esos filtros.").FontSize(11);
                        }
                        foreach (IGrouping<string, FichaHojaMuestra> familia in familias)
                        {
                            columna.Item().PaddingTop(8).PaddingBottom(4).BorderBottom(1).Text(familia.Key).Bold().FontSize(12);
                            columna.Item().Table(tabla =>
                            {
                                tabla.ColumnsDefinition(c =>
                                {
                                    for (int i = 0; i < COLUMNAS; i++)
                                    {
                                        c.RelativeColumn();
                                    }
                                });
                                foreach (FichaHojaMuestra muestra in familia)
                                {
                                    tabla.Cell().Padding(3).Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(4)
                                        .Element(c => Ficha(c, muestra));
                                }
                            });
                        }
                    });
                    pagina.Footer().AlignRight().Text(t =>
                    {
                        t.Span("Página ");
                        t.CurrentPageNumber();
                        t.Span(" de ");
                        t.TotalPages();
                    });
                });
            }).GeneratePdf();
        }

        private static void Cabecera(IContainer contenedor, HojaMuestras hoja)
        {
            contenedor.PaddingBottom(4).Column(c =>
            {
                c.Item().Text("Muestras" + (hoja.Familia == null ? string.Empty : " de " + hoja.Familia)).Bold().FontSize(15);
                string almacen = hoja.Almacen == null ? "todos los almacenes" : hoja.Almacen;
                c.Item().Text($"{almacen}{(hoja.SoloConStock ? ", solo con stock" : string.Empty)} · {(hoja.Muestras ?? new List<FichaHojaMuestra>()).Count} muestras · " +
                    $"{hoja.Fecha.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("es-ES"))}").FontSize(8);
            });
        }

        private static void Ficha(IContainer contenedor, FichaHojaMuestra muestra)
        {
            contenedor.Column(c =>
            {
                if (muestra.Foto != null)
                {
                    try
                    {
                        c.Item().Height(ALTO_FOTO).AlignCenter().Image(muestra.Foto, ImageScaling.FitArea);
                    }
                    catch
                    {
                        // Una imagen que QuestPDF no entiende: sin foto
                    }
                }
                c.Item().Text(muestra.Nombre ?? string.Empty).Bold().FontSize(9);
                c.Item().Text("Ref. " + muestra.Producto).FontSize(8);
                if (Ean13.EsValido(muestra.CodigoBarras))
                {
                    c.Item().PaddingTop(3).AlignCenter().Element(e => DibujarEan13(e, muestra.CodigoBarras));
                }
                else
                {
                    c.Item().PaddingTop(3).Text("Sin código de barras: dele código («Dar código a las muestras»).").Italic().FontSize(7);
                }
            });
        }

        /// <summary>El EAN-13 en el lienzo (Skia, que ya trae QuestPDF), con sus zonas de silencio y las 13 cifras debajo.</summary>
        internal static void DibujarEan13(IContainer contenedor, string codigo)
        {
            List<(bool EsBarra, int Modulos)> tramos = Ean13.Tramos(codigo);
            int modulos = Ean13.SILENCIO_IZQUIERDA + Ean13.MODULOS + Ean13.SILENCIO_DERECHA;
            contenedor.Column(col =>
            {
                col.Item().Width(modulos * MODULO).Height(ALTO_BARRAS).Canvas((lienzo, tamano) =>
                {
                    using (var pincel = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.Black, IsAntialias = false, Style = SkiaSharp.SKPaintStyle.Fill })
                    {
                        float x = Ean13.SILENCIO_IZQUIERDA * MODULO;
                        foreach ((bool esBarra, int ancho) in tramos)
                        {
                            float w = ancho * MODULO;
                            if (esBarra)
                            {
                                lienzo.DrawRect(x, 0, w, ALTO_BARRAS, pincel);
                            }
                            x += w;
                        }
                    }
                });
                col.Item().Width(modulos * MODULO).AlignCenter().Text(codigo.Trim()).FontSize(8);
            });
        }
    }
}
