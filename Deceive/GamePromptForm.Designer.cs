namespace Deceive
{
    internal partial class GamePromptForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(GamePromptForm));
            this.label1 = new System.Windows.Forms.Label();
            this.buttonLaunchLoL = new System.Windows.Forms.Button();
            this.buttonLaunchLoR = new System.Windows.Forms.Button();
            this.buttonLaunchValorant = new System.Windows.Forms.Button();
            this.buttonLaunchRiotClient = new System.Windows.Forms.Button();
            this.checkboxRemember = new System.Windows.Forms.CheckBox();
            this.panelFootnoteCheck = new System.Windows.Forms.Panel();
            this.panelFootnoteCheck.SuspendLayout();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Segoe UI", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label1.Location = new System.Drawing.Point(85, 9);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(338, 25);
            this.label1.TabIndex = 0;
            this.label1.Text = "Which game would you like to launch?";
            // 
            // buttonLaunchLoL
            // 
            this.buttonLaunchLoL.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("buttonLaunchLoL.BackgroundImage")));
            this.buttonLaunchLoL.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.buttonLaunchLoL.FlatAppearance.BorderColor = System.Drawing.SystemColors.Control;
            this.buttonLaunchLoL.FlatAppearance.MouseDownBackColor = System.Drawing.Color.SandyBrown;
            this.buttonLaunchLoL.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(224)))), ((int)(((byte)(192)))));
            this.buttonLaunchLoL.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.buttonLaunchLoL.Location = new System.Drawing.Point(30, 49);
            this.buttonLaunchLoL.Name = "buttonLaunchLoL";
            this.buttonLaunchLoL.Size = new System.Drawing.Size(221, 91);
            this.buttonLaunchLoL.TabIndex = 1;
            this.buttonLaunchLoL.UseVisualStyleBackColor = true;
            this.buttonLaunchLoL.Click += new System.EventHandler(this.OnLoLLaunch);
            // 
            // buttonLaunchLoR
            // 
            this.buttonLaunchLoR.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("buttonLaunchLoR.BackgroundImage")));
            this.buttonLaunchLoR.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.buttonLaunchLoR.FlatAppearance.BorderColor = System.Drawing.SystemColors.Control;
            this.buttonLaunchLoR.FlatAppearance.MouseDownBackColor = System.Drawing.Color.SandyBrown;
            this.buttonLaunchLoR.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(224)))), ((int)(((byte)(192)))));
            this.buttonLaunchLoR.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.buttonLaunchLoR.Location = new System.Drawing.Point(257, 49);
            this.buttonLaunchLoR.Name = "buttonLaunchLoR";
            this.buttonLaunchLoR.Size = new System.Drawing.Size(221, 91);
            this.buttonLaunchLoR.TabIndex = 2;
            this.buttonLaunchLoR.UseVisualStyleBackColor = true;
            this.buttonLaunchLoR.Click += new System.EventHandler(this.OnLoRLaunch);
            // 
            // buttonLaunchValorant
            // 
            this.buttonLaunchValorant.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("buttonLaunchValorant.BackgroundImage")));
            this.buttonLaunchValorant.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.buttonLaunchValorant.FlatAppearance.BorderColor = System.Drawing.SystemColors.Control;
            this.buttonLaunchValorant.FlatAppearance.MouseDownBackColor = System.Drawing.Color.DarkRed;
            this.buttonLaunchValorant.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(192)))), ((int)(((byte)(192)))));
            this.buttonLaunchValorant.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.buttonLaunchValorant.Location = new System.Drawing.Point(30, 146);
            this.buttonLaunchValorant.Name = "buttonLaunchValorant";
            this.buttonLaunchValorant.Size = new System.Drawing.Size(221, 91);
            this.buttonLaunchValorant.TabIndex = 3;
            this.buttonLaunchValorant.UseVisualStyleBackColor = true;
            this.buttonLaunchValorant.Click += new System.EventHandler(this.OnValorantLaunch);
            // 
            // buttonLaunchRiotClient
            // 
            this.buttonLaunchRiotClient.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("buttonLaunchRiotClient.BackgroundImage")));
            this.buttonLaunchRiotClient.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.buttonLaunchRiotClient.FlatAppearance.BorderColor = System.Drawing.SystemColors.Control;
            this.buttonLaunchRiotClient.FlatAppearance.MouseDownBackColor = System.Drawing.Color.DarkRed;
            this.buttonLaunchRiotClient.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(192)))), ((int)(((byte)(192)))));
            this.buttonLaunchRiotClient.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.buttonLaunchRiotClient.Location = new System.Drawing.Point(257, 146);
            this.buttonLaunchRiotClient.Name = "buttonLaunchRiotClient";
            this.buttonLaunchRiotClient.Size = new System.Drawing.Size(221, 91);
            this.buttonLaunchRiotClient.TabIndex = 4;
            this.buttonLaunchRiotClient.UseVisualStyleBackColor = true;
            this.buttonLaunchRiotClient.Click += new System.EventHandler(this.OnRiotClientLaunch);
            // 
            // checkboxRemember
            // 
            this.checkboxRemember.AutoSize = true;
            this.checkboxRemember.Font = new System.Drawing.Font("Segoe UI", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.checkboxRemember.Location = new System.Drawing.Point(17, 9);
            this.checkboxRemember.Name = "checkboxRemember";
            this.checkboxRemember.Size = new System.Drawing.Size(444, 24);
            this.checkboxRemember.TabIndex = 5;
            this.checkboxRemember.Text = "Remember my decision and skip this screen on future launches.";
            this.checkboxRemember.UseVisualStyleBackColor = true;
            // 
            // panelFootnoteCheck
            // 
            this.panelFootnoteCheck.BackColor = System.Drawing.SystemColors.ControlLight;
            this.panelFootnoteCheck.Controls.Add(this.checkboxRemember);
            this.panelFootnoteCheck.Location = new System.Drawing.Point(-5, 254);
            this.panelFootnoteCheck.Name = "panelFootnoteCheck";
            this.panelFootnoteCheck.Size = new System.Drawing.Size(520, 49);
            this.panelFootnoteCheck.TabIndex = 6;
            // 
            // GamePromptForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(509, 297);
            this.Controls.Add(this.buttonLaunchRiotClient);
            this.Controls.Add(this.buttonLaunchValorant);
            this.Controls.Add(this.buttonLaunchLoR);
            this.Controls.Add(this.buttonLaunchLoL);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.panelFootnoteCheck);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "GamePromptForm";
            this.Text = "Deceive";
            this.Load += new System.EventHandler(this.OnFormLoad);
            this.panelFootnoteCheck.ResumeLayout(false);
            this.panelFootnoteCheck.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button buttonLaunchLoL;
        private System.Windows.Forms.Button buttonLaunchLoR;
        private System.Windows.Forms.Button buttonLaunchValorant;
        private System.Windows.Forms.Button buttonLaunchRiotClient;
        private System.Windows.Forms.CheckBox checkboxRemember;
        private System.Windows.Forms.Panel panelFootnoteCheck;
    }
}