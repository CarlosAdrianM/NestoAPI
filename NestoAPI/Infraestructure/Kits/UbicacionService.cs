using NestoAPI.Models;
using NestoAPI.Models.Kits;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Kits
{
    public class UbicacionService : IUbicacionService
    {
        public async Task<int> PersistirMontarKit(List<PreExtractoProductoDTO> preExtractosUbicados)
        {
            using (var db = new NVEntities())
            {
                using (var transaction = db.Database.BeginTransaction())
                {
                    var contador = db.ContadoresGlobales.Single();
                    var traspaso = ++contador.TraspasoAlmacén;

                    try
                    {
                        // Parte 1: Insertar registros en una tabla
                        // ... lógica para crear nuevos registros

                        foreach (var preExtracto in preExtractosUbicados)
                        {
                            bool elAlmacenTieneControlDeUbicaciones = preExtracto.Almacen == Constantes.Almacenes.ALGETE;
                            foreach (var ubicacion in preExtracto.Ubicaciones)
                            {
                                if (ubicacion.Estado == Constantes.Ubicaciones.ESTADO_REGISTRO_MONTAR_KITS)
                                {
                                    db.PreExtrProductos.Add(new PreExtrProducto
                                    {
                                        Empresa = preExtracto.Empresa,
                                        Diario = preExtracto.Diario,
                                        Número = preExtracto.Producto,
                                        Fecha = DateTime.Now,
                                        Almacén = preExtracto.Almacen,
                                        Grupo = preExtracto.Grupo,
                                        Cantidad = (short)ubicacion.Cantidad,
                                        Asiento_Automático = true,
                                        Estado = 1,
                                        Pasillo = ubicacion.Pasillo,
                                        Fila = ubicacion.Fila,
                                        Columna = ubicacion.Columna,
                                        NºTraspaso = traspaso,
                                        Delegación = preExtracto.Almacen, // habría que leerlo de los parámetros
                                        Forma_Venta = Constantes.Empresas.FORMA_VENTA_POR_DEFECTO,
                                        Texto = preExtracto.Texto,
                                        Usuario = preExtracto.Usuario,
                                        Fecha_Modificación = DateTime.Now
                                    });
                                }
                                if (ubicacion.Id == 0 && elAlmacenTieneControlDeUbicaciones)
                                {
                                    var nuevaUbicacion = new Ubicacion
                                    {
                                        Empresa = ubicacion.Empresa,
                                        Almacén = ubicacion.Almacen,
                                        Número = ubicacion.Producto,
                                        Estado = ubicacion.Estado,
                                        NºTraspaso = traspaso,
                                        Pasillo = ubicacion.Pasillo,
                                        Fila = ubicacion.Fila,
                                        Columna = ubicacion.Columna,
                                        Cantidad = ubicacion.Cantidad,
                                        Usuario = preExtracto.Usuario
                                    };
                                    db.Ubicaciones.Add(nuevaUbicacion);
                                } 
                                else if (ubicacion.Estado == Constantes.Ubicaciones.ESTADO_REGISTRO_MONTAR_KITS && elAlmacenTieneControlDeUbicaciones)
                                {
                                    var ubicacionModificada = db.Ubicaciones.Find(ubicacion.Id);

                                    if (ubicacionModificada != null)
                                    {
                                        ubicacionModificada.Estado = (short)ubicacion.Estado;
                                        ubicacionModificada.NºTraspaso = traspaso;

                                        // Marcar los campos modificados
                                        db.Entry(ubicacionModificada).Property(x => x.Estado).IsModified = true;
                                        db.Entry(ubicacionModificada).Property(x => x.NºTraspaso).IsModified = true;
                                    }                                    
                                }
                                else if (ubicacion.Estado == Constantes.Ubicaciones.ESTADO_A_MODIFICAR_CANTIDAD && elAlmacenTieneControlDeUbicaciones)
                                {
                                    // Obtener la entidad existente del contexto por su clave primaria
                                    var ubicacionModificada = db.Ubicaciones.Find(ubicacion.Id);

                                    // Verificar si la entidad existe antes de intentar modificarla
                                    if (ubicacionModificada != null)
                                    {
                                        ubicacionModificada.Cantidad = (short)ubicacion.Cantidad;
                                        ubicacionModificada.NºTraspaso = traspaso;

                                        if (string.IsNullOrEmpty(ubicacion.Pasillo) && string.IsNullOrEmpty(ubicacion.Fila) && string.IsNullOrEmpty(ubicacion.Columna))
                                        {
                                            ubicacionModificada.Estado = Constantes.Ubicaciones.PENDIENTE_UBICAR;
                                        }
                                        else
                                        {
                                            ubicacionModificada.Estado = Constantes.Ubicaciones.UBICADO;
                                        }

                                        // Marcar los campos modificados
                                        db.Entry(ubicacionModificada).Property(x => x.Cantidad).IsModified = true;
                                        db.Entry(ubicacionModificada).Property(x => x.NºTraspaso).IsModified = true;
                                        db.Entry(ubicacionModificada).Property(x => x.Estado).IsModified = true;
                                    }
                                }
                                else
                                {
                                    throw new Exception("Estado no contemplado en ubicaciones");
                                }
                            }
                        }
                        db.SaveChanges();

                        // Parte 2: Ejecutar el procedimiento almacenado
                        var empresaParametro = new SqlParameter("@Empresa", SqlDbType.Char, 3)
                        {
                            Value = preExtractosUbicados[0].Empresa
                        };

                        var diarioParametro = new SqlParameter("@Diario", SqlDbType.Char, 10)
                        {
                            Value = preExtractosUbicados[0].Diario
                        };
                        var resultadoProcedimiento = await db.Database.ExecuteSqlCommandAsync("EXEC prdExtrProducto @Empresa, @Diario", empresaParametro, diarioParametro);

                        // Verificar el resultado del procedimiento almacenado si es necesario
                        if (resultadoProcedimiento <= 0)
                        {
                            throw new Exception("El procedimiento almacenado no se ejecutó correctamente.");
                        }

                        // Commit de la transacción si todo ha ido bien
                        transaction.Commit();
                        return traspaso;
                    }
                    catch (Exception ex)
                    {
                        // Algo salió mal, realizar un rollback
                        transaction.Rollback();
                        Console.WriteLine($"Error: {ex.Message}");
                        return 0;
                    }
                }
            }

        }

        public async Task<List<UbicacionProductoDTO>> LeerUbicacionesProducto(string producto)
        {
            var almacen = Constantes.Almacenes.ALGETE;
            using (var db = new NVEntities())
            {
                return await db.Ubicaciones
                    .Where(u => u.Almacén == almacen && u.Número == producto && (u.Estado == Constantes.Ubicaciones.UBICADO || u.Estado == Constantes.Ubicaciones.PENDIENTE_UBICAR))
                    .OrderBy(u => u.NºOrden)
                    .Select(u => new UbicacionProductoDTO
                    {
                        Id = u.NºOrden,
                        Empresa = u.Empresa.Trim(),
                        Almacen = u.Almacén.Trim(),
                        Producto = u.Número.Trim(),
                        Cantidad = u.Cantidad,
                        Pasillo = u.Pasillo,
                        Fila = u.Fila,
                        Columna = u.Columna,
                        Estado = (int)u.Estado
                    }).ToListAsync();
            }
        }
    }
}