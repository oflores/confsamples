using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using System.Net;

using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.StorageClient;

using FotoGol_Data;

namespace FotoGol_WebRole
{
    public partial class _Default : System.Web.UI.Page
    {
        private static bool storageInitialized = false;
        private static object gate = new Object( );
        private static CloudBlobClient blobStorage;
        private static CloudQueueClient queueStorage;

        protected void Page_Load( object sender, EventArgs e )
        {
            if ( !Page.IsPostBack )
            {
                Timer1.Enabled = true;
            }
        }

        protected void SignButton_Click( object sender, EventArgs e )
        {
            if ( FileUpload1.HasFile )
            {
                InitializeStorage( );

                //Subo la imagen al Blob Storage
                CloudBlobContainer container = blobStorage.GetContainerReference( "fotogolpics" );
                string uniqueBlobName = string.Format( "image_{0}.jpg", Guid.NewGuid( ).ToString( ) );
                CloudBlockBlob blob = container.GetBlockBlobReference( uniqueBlobName );
                blob.Properties.ContentType = FileUpload1.PostedFile.ContentType;
                blob.UploadFromStream( FileUpload1.FileContent );
                System.Diagnostics.Trace.TraceInformation( "Uploaded image '{0}' to blob storage as '{1}'", FileUpload1.FileName, uniqueBlobName );

                //Creo un nuevo registro en la tabla
                FotoGolEntry entry = new FotoGolEntry( ) { GuestName = NameTextBox.Text, Message = MessageTextBox.Text, PhotoUrl = blob.Uri.ToString( ), ThumbnailUrl = blob.Uri.ToString( ) };
                FotoGolEntryDataSource ds = new FotoGolEntryDataSource( );
                ds.AddGuestBookEntry( entry );
                System.Diagnostics.Trace.TraceInformation( "Added entry {0}-{1} in table storage for guest '{2}'", entry.PartitionKey, entry.RowKey, entry.GuestName );

                //Pongo un mensaje en cola para que se procese
                var queue = queueStorage.GetQueueReference( "fotogolthumbs" );
                var message = new CloudQueueMessage( String.Format( "{0},{1},{2}", uniqueBlobName, entry.PartitionKey, entry.RowKey ) );
                queue.AddMessage( message );
                System.Diagnostics.Trace.TraceInformation( "Queued message to process blob '{0}'", uniqueBlobName );
            }

            NameTextBox.Text = "";
            MessageTextBox.Text = "";

            DataList1.DataBind( );
        }

        protected void Timer1_Tick( object sender, EventArgs e )
        {
            DataList1.DataBind( );
        }

        private void InitializeStorage( )
        {
            if ( storageInitialized )
            {
                return;
            }

            lock ( gate )
            {
                if ( storageInitialized )
                {
                    return;
                }

                try
                {
                    // read account configuration settings
                    var storageAccount = CloudStorageAccount.FromConfigurationSetting( "DataConnectionString" );

                    // create blob container for images
                    blobStorage = storageAccount.CreateCloudBlobClient( );
                    CloudBlobContainer container = blobStorage.GetContainerReference( "fotogolpics" );
                    container.CreateIfNotExist( );

                    // configure container for public access
                    var permissions = container.GetPermissions( );
                    permissions.PublicAccess = BlobContainerPublicAccessType.Container;
                    container.SetPermissions( permissions );

                    // create queue to communicate with worker role
                    queueStorage = storageAccount.CreateCloudQueueClient( );
                    CloudQueue queue = queueStorage.GetQueueReference( "fotogolthumbs" );
                    queue.CreateIfNotExist( );
                }
                catch ( WebException )
                {
                    throw new WebException( "Storage services initialization failure. "
                        + "Check your storage account configuration settings. If running locally, "
                        + "ensure that the Development Storage service is running." );
                }

                storageInitialized = true;
            }
        }
    }
}